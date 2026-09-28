#!/usr/bin/env python3
"""Disposable #139 continuity spike. Not a production authority."""
import hashlib
import json
import os
import secrets
import socket
import socketserver
import struct
import sys
import threading
import uuid
import fcntl
from contextlib import contextmanager
from pathlib import Path

REGISTRY = Path('/proof/service/control/registry-v6.json')
ANCHOR = Path('/anchor/authority/anchor-v6.json')
SOCKET = Path('/proof/socket/authority-v6.sock')
LOCKFILE = Path('/proof/service/control/authority-v6.lock')
ALLOWED_CHECKOUT = '/proof/service/checkout'
SERVICE_UID = 2001
CLIENT_UIDS = {2002, 2003}
LOCK = threading.Lock()


@contextmanager
def exclusive_authority():
    descriptor = os.open(LOCKFILE, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield
    finally:
        os.close(descriptor)


def encoded(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode()


def durable_replace(path, value):
    data = encoded(value)
    temporary = path.with_name(path.name + '.' + secrets.token_hex(8) + '.tmp')
    with temporary.open('xb') as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def provision():
    with exclusive_authority():
        if REGISTRY.exists() or ANCHOR.exists():
            raise RuntimeError('authority already provisioned or partially provisioned')
        identity, canonical = physical_binding(ALLOWED_CHECKOUT)
        nonce = secrets.token_hex(32)
        registry = {'nonce': nonce, 'sequence': 0, 'generation': 1,
                    'provisionedIdentity': identity,
                    'provisionedPath': canonical, 'bindings': []}
        anchor = {'nonce': nonce, 'sequence': 0,
                  'provisionedIdentity': identity,
                  'digest': hashlib.sha256(encoded(registry)).hexdigest()}
        durable_replace(REGISTRY, registry)
        durable_replace(ANCHOR, anchor)
        print(json.dumps({'provisioned': True, 'nonce': nonce}, sort_keys=True))


def load_verified():
    try:
        registry = json.loads(REGISTRY.read_bytes())
        anchor = json.loads(ANCHOR.read_bytes())
        if (registry['nonce'] != anchor['nonce']
                or registry['sequence'] != anchor['sequence']
                or registry['provisionedIdentity'] != anchor['provisionedIdentity']
                or hashlib.sha256(encoded(registry)).hexdigest() != anchor['digest']):
            raise ValueError('registry continuity mismatch')
        return registry, anchor
    except (OSError, ValueError, KeyError, TypeError):
        raise ValueError('RejectedUnsafeCoordination') from None


def physical_binding(path):
    flags = os.O_PATH | os.O_DIRECTORY | os.O_NOFOLLOW
    checkout = os.open(path, flags)
    try:
        common = os.open('.git', flags, dir_fd=checkout)
        try:
            root_stat, common_stat = os.fstat(checkout), os.fstat(common)
            identity = [root_stat.st_dev, root_stat.st_ino,
                        common_stat.st_dev, common_stat.st_ino]
            canonical = os.readlink(f'/proc/self/fd/{checkout}')
            if root_stat.st_uid != SERVICE_UID or common_stat.st_uid != SERVICE_UID:
                raise ValueError('checkout is not service-owned')
            for component in [Path(canonical), *Path(canonical).parents]:
                metadata = component.stat()
                if metadata.st_uid not in (0, SERVICE_UID) or metadata.st_mode & 0o022:
                    raise ValueError('checkout ancestor is writable by another principal')
            if common_stat.st_mode & 0o022:
                raise ValueError('Git common directory is writable by another principal')
            return identity, canonical
        finally:
            os.close(common)
    finally:
        os.close(checkout)


def publish(registry):
    registry['sequence'] += 1
    anchor = {'nonce': registry['nonce'], 'sequence': registry['sequence'],
              'provisionedIdentity': registry['provisionedIdentity'],
              'digest': hashlib.sha256(encoded(registry)).hexdigest()}
    durable_replace(REGISTRY, registry)
    if os.environ.get('RT_PROOF_CRASH_AFTER_REGISTRY') == '1':
        os._exit(73)
    durable_replace(ANCHOR, anchor)


def process(uid, request):
    if uid not in CLIENT_UIDS | {SERVICE_UID}:
        return {'error': 'OperationAccessDenied'}
    try:
        registry, _ = load_verified()
    except ValueError:
        return {'error': 'RejectedUnsafeCoordination'}
    action = request.get('action')
    if action == 'barrier':
        if uid != SERVICE_UID:
            return {'error': 'OperationAccessDenied'}
        target = request.get('repositoryId')
        for item in registry['bindings']:
            if item['repositoryId'] == target:
                item['recoveryRequired'] = True
                publish(registry)
                return {'state': 'RecoveryRequired', 'sequence': registry['sequence']}
        return {'error': 'RepositoryNotEnrolled'}
    if action not in {'lookup', 'enroll', 'admit'}:
        return {'error': 'InvalidInput'}
    try:
        identity, canonical = physical_binding(request['path'])
    except (OSError, ValueError, KeyError, TypeError):
        return {'error': 'RejectedUnsafeCoordination'}
    if identity != registry['provisionedIdentity']:
        return {'error': 'RejectedUnsafeCoordination'}
    existing = next((item for item in registry['bindings']
                     if item['physicalIdentity'] == identity), None)
    if existing is None:
        if any(item['canonicalPath'] == canonical for item in registry['bindings']):
            return {'error': 'ConflictingBinding'}
        if action != 'enroll':
            return {'error': 'RepositoryNotEnrolled'}
        if request.get('expectedId'):
            return {'error': 'ConflictingBinding'}
        existing = {'repositoryId': uuid.uuid4().hex,
                    'physicalIdentity': identity,
                    'canonicalPath': canonical,
                    'recoveryRequired': False}
        registry['bindings'].append(existing)
        publish(registry)
    elif request.get('expectedId') and request['expectedId'] != existing['repositoryId']:
        return {'error': 'ConflictingBinding'}
    if action == 'admit' and existing['recoveryRequired']:
        return {'error': 'RecoveryRequired'}
    return {'repositoryId': existing['repositoryId'],
            'generation': registry['generation'],
            'sequence': registry['sequence'],
            'recoveryRequired': existing['recoveryRequired'],
            'state': 'Admitted' if action == 'admit' else 'Enrolled'}


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        credentials = self.request.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12)
        _, uid, _ = struct.unpack('3i', credentials)
        try:
            request = json.loads(self.rfile.readline())
            with LOCK:
                result = process(uid, request)
        except (ValueError, KeyError, TypeError, OSError):
            result = {'error': 'RejectedUnsafeCoordination'}
        self.wfile.write(encoded(result) + b'\n')


class Server(socketserver.ThreadingUnixStreamServer):
    daemon_threads = True


def serve():
    with exclusive_authority():
        try:
            SOCKET.unlink()
        except FileNotFoundError:
            pass
        with Server(str(SOCKET), Handler) as server:
            os.chmod(SOCKET, 0o666)
            print('ready', flush=True)
            server.serve_forever()


def call(action, path='', extra=''):
    request = {'action': action}
    if path:
        request['path'] = path
    if extra:
        request['repositoryId' if action == 'barrier' else 'expectedId'] = extra
    try:
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
            client.connect(str(SOCKET))
            client.sendall(encoded(request) + b'\n')
            response = bytearray()
            while not response.endswith(b'\n'):
                part = client.recv(4096)
                if not part:
                    raise OSError('authority closed connection')
                response.extend(part)
        result = json.loads(response)
        print(json.dumps(result, sort_keys=True, separators=(',', ':')))
        if 'error' in result:
            raise SystemExit(2)
    except (OSError, ValueError):
        print('{"error":"RejectedUnsafeCoordination"}')
        raise SystemExit(2)


if __name__ == '__main__':
    command = sys.argv[1]
    if command == 'provision':
        provision()
    elif command == 'serve':
        serve()
    elif command == 'call':
        call(*sys.argv[2:])
    else:
        raise SystemExit('unsupported command')
