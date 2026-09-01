#!/usr/bin/env bash

# Builds, verifies, and publishes the site under gh-pages:/docs.
set -euo pipefail

if [[ "$(git branch --show-current)" != "main" ]]; then
  echo 'Publish from the main branch.' >&2
  exit 1
fi

if [[ -n "$(git status --porcelain)" ]]; then
  echo 'Commit or stash repository changes before publishing.' >&2
  exit 1
fi

npm test
git pull --ff-only origin main
git push origin main
git fetch origin gh-pages

deploy_worktree="$(mktemp -d)"
trap 'git worktree remove "$deploy_worktree" --force >/dev/null 2>&1 || true' EXIT
git worktree add --detach "$deploy_worktree" origin/gh-pages

deploy_docs="$deploy_worktree/docs"
case "$deploy_docs" in
  "$deploy_worktree"/docs) ;;
  *) echo 'Refusing to replace docs outside the deployment worktree.' >&2; exit 1 ;;
esac

rm -rf "$deploy_docs"
mkdir -p "$deploy_docs"
cp -a artifacts/site/. "$deploy_docs/"
(
  cd "$deploy_worktree"
  git add -f -A docs
  if [[ -n "$(git status --porcelain)" ]]; then
    git commit -m "Deploy documentation $(date '+%Y-%m-%d %H:%M:%S')"
    git push origin HEAD:gh-pages --force
  fi
)

echo 'Documentation published to https://roslyn-tools.danmarshall.dev.'
