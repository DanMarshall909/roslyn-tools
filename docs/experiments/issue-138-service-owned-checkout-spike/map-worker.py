import mmap
from pathlib import Path
p = Path('/proof/editor-home/mapped.cs')
with p.open('r+b') as file:
    mapping = mmap.mmap(file.fileno(), 4096, access=mmap.ACCESS_WRITE)
    Path('/proof/editor-home/map-ready').touch()
    while not Path('/proof/editor-home/map-go').exists():
        import time
        time.sleep(0.1)
    mapping[:4] = b'EDIT'
    mapping.flush()
    mapping.close()
