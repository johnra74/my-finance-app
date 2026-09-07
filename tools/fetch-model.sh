#!/usr/bin/env bash
# Fetches the sentence-embedding model used for category suggestions.
#
# The weights are ~23 MB and are deliberately NOT committed: they are a build input, fetched
# once and verified against a pinned hash. A model that changed underneath us would not fail
# loudly — it would quietly start making different suggestions — so the hash is the point of
# this script, not the download.
set -euo pipefail

cd "$(dirname "$0")/.."
DEST=src/MyFinance.Semantics/Assets
REPO=https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main

mkdir -p "$DEST"

# file:remote-path:sha256
FILES=(
  "model_quantized.onnx:onnx/model_quantized.onnx:afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1"
  "vocab.txt:vocab.txt:07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3"
)

for entry in "${FILES[@]}"; do
  name="${entry%%:*}"
  rest="${entry#*:}"
  remote="${rest%%:*}"
  want="${rest#*:}"
  target="$DEST/$name"

  if [[ -f "$target" ]]; then
    have=$(sha256sum "$target" | cut -d' ' -f1)
    if [[ "$have" == "$want" ]]; then
      echo "ok       $name"
      continue
    fi
    echo "stale    $name (re-fetching)"
  fi

  echo "fetching $name ..."
  curl -sSL --fail -o "$target.partial" "$REPO/$remote"
  have=$(sha256sum "$target.partial" | cut -d' ' -f1)

  if [[ "$want" != HASH_* && "$have" != "$want" ]]; then
    rm -f "$target.partial"
    echo "FAILED   $name: expected $want, got $have" >&2
    exit 1
  fi

  mv "$target.partial" "$target"
  echo "ok       $name  sha256=$have"
done
