# Third-party notices — MyFinance.Semantics

## all-MiniLM-L6-v2

Category suggestions use the **all-MiniLM-L6-v2** sentence-embedding model, in the int8
ONNX export published as `Xenova/all-MiniLM-L6-v2` (`onnx/model_quantized.onnx`), together
with its WordPiece vocabulary.

- Original model: <https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>
- ONNX export used here: <https://huggingface.co/Xenova/all-MiniLM-L6-v2>
- Licence: **Apache License 2.0**

The weights are not kept in this repository. `tools/fetch-model.sh` downloads them and
verifies these hashes:

| File | SHA-256 |
|---|---|
| `model_quantized.onnx` | `afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1` |
| `vocab.txt` | `07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3` |

The model runs entirely on the machine. No transaction text is sent anywhere.

## ONNX Runtime

Inference uses **Microsoft.ML.OnnxRuntime**, and tokenization **Microsoft.ML.Tokenizers**,
both under the **MIT Licence**. <https://github.com/microsoft/onnxruntime>
