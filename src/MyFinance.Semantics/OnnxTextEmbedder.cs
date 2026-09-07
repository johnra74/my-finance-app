using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using MyFinance.Import.Categorization;

namespace MyFinance.Semantics;

/// <summary>
/// Embeds transaction text with a small sentence-transformer running locally.
/// </summary>
/// <remarks>
/// <para>
/// all-MiniLM-L6-v2, quantised to int8 and embedded in this assembly. It runs entirely on
/// the machine: no text ever leaves it, which for a file full of somebody's spending is the
/// only acceptable arrangement.
/// </para>
/// <para>
/// Every failure path here is soft. A machine whose CPU or runtime cannot load the model
/// gets <see cref="IsAvailable" /> false and loses one suggestion source; it does not get an
/// exception in the middle of importing a statement. That matters more than usual because
/// the single-file Windows build loads a second native library beside SQLCipher, and that
/// combination cannot be tested from the machine this was written on.
/// </para>
/// </remarks>
public sealed class OnnxTextEmbedder : ITextEmbedder, IDisposable
{
    /// <summary>Output width of all-MiniLM-L6-v2.</summary>
    public const int VectorSize = 384;

    /// <summary>
    /// Longest input in word pieces.
    /// </summary>
    /// <remarks>
    /// The model accepts 256, but a bank descriptor is a dozen tokens at most. Capping short
    /// keeps the padded batch small, and the cost of a forward pass is quadratic in length.
    /// </remarks>
    public const int MaxTokens = 64;

    /// <summary>
    /// How many texts go through the model at once. One, deliberately.
    /// </summary>
    /// <remarks>
    /// The int8 export is <em>dynamically</em> quantised: activation scales are computed at
    /// run time from the batch in hand, so the same text embeds slightly differently
    /// depending on what it was batched with — measured here at a cosine of 0.989 to 0.992
    /// between batched and unbatched. That is small, but it means the category suggested for
    /// a transaction would depend on how many other rows happened to be in the same
    /// statement. A book that answers differently on a re-import is not one anybody can
    /// check, so throughput is traded for determinism.
    /// </remarks>
    private const int BatchSize = 1;

    private readonly InferenceSession? _session;
    private readonly BertTokenizer? _tokenizer;
    private readonly Lock _gate = new();
    private bool _disposed;

    private OnnxTextEmbedder(InferenceSession? session, BertTokenizer? tokenizer, string? failure)
    {
        _session = session;
        _tokenizer = tokenizer;
        Failure = failure;
    }

    /// <summary>Why the model is unavailable, when it is. Null when all is well.</summary>
    public string? Failure { get; }

    public int Dimensions => VectorSize;

    public bool IsAvailable => _session is not null && _tokenizer is not null && !_disposed;

    /// <summary>
    /// Loads the model, or returns one that politely does nothing.
    /// </summary>
    /// <remarks>
    /// Never throws. The caller is a personal finance application starting up, and there is
    /// no version of "could not load a suggestion model" that justifies refusing to open
    /// somebody's accounts.
    /// </remarks>
    public static OnnxTextEmbedder Create()
    {
        try
        {
            using Stream model = OpenResource("model.onnx")
                ?? throw new InvalidOperationException(
                    "The model was not embedded in this build. Run tools/fetch-model.sh "
                    + "-- tools\\fetch-model on Windows -- and rebuild.");

            using Stream vocab = OpenResource("vocab.txt")
                ?? throw new InvalidOperationException("The tokenizer vocabulary was not embedded in this build.");

            var buffer = new MemoryStream();
            model.CopyTo(buffer);

            // Single-threaded: this runs beside a UI and alongside an import, and letting
            // ONNX spawn a thread per core to embed a dozen short strings costs more in
            // scheduling than it saves.
            var options = new SessionOptions
            {
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };

            var session = new InferenceSession(buffer.ToArray(), options);
            BertTokenizer tokenizer = BertTokenizer.Create(vocab);

            return new OnnxTextEmbedder(session, tokenizer, null);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException
                                      or DllNotFoundException
                                      or BadImageFormatException
                                      or TypeInitializationException
                                      or InvalidOperationException
                                      or IOException)
        {
            return new OnnxTextEmbedder(null, null, ex.Message);
        }
    }

    private static Stream? OpenResource(string name) =>
        typeof(OnnxTextEmbedder).Assembly.GetManifestResourceStream(name);

    public IReadOnlyList<float[]> Embed(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (!IsAvailable || texts.Count == 0)
        {
            return [];
        }

        var results = new float[texts.Count][];

        // One session, used from one thread at a time. ONNX sessions are thread-safe for
        // inference, but serialising here keeps memory predictable while a background index
        // build and an import can both want it.
        lock (_gate)
        {
            for (int start = 0; start < texts.Count; start += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int count = Math.Min(BatchSize, texts.Count - start);
                EmbedBatch(texts, start, count, results);
            }
        }

        return results;
    }

    private void EmbedBatch(IReadOnlyList<string> texts, int start, int count, float[][] results)
    {
        var encoded = new List<IReadOnlyList<int>>(count);
        int longest = 1;

        for (int i = 0; i < count; i++)
        {
            IReadOnlyList<int> ids = _tokenizer!.EncodeToIds(
                texts[start + i] ?? string.Empty,
                maxTokenCount: MaxTokens,
                out _,
                out _);

            encoded.Add(ids);
            longest = Math.Max(longest, ids.Count);
        }

        var inputIds = new DenseTensor<long>([count, longest]);
        var attention = new DenseTensor<long>([count, longest]);
        var tokenTypes = new DenseTensor<long>([count, longest]);

        for (int i = 0; i < count; i++)
        {
            IReadOnlyList<int> ids = encoded[i];

            for (int t = 0; t < ids.Count; t++)
            {
                inputIds[i, t] = ids[t];
                attention[i, t] = 1;
            }

            // Everything past the real tokens stays zero: id zero is [PAD] and an attention
            // of zero is what keeps the padding out of the pooled average.
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output = _session!.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attention),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypes),
        ]);

        Tensor<float> hidden = output.First().AsTensor<float>();

        for (int i = 0; i < count; i++)
        {
            results[start + i] = Pool(hidden, attention, i, longest);
        }
    }

    /// <summary>
    /// Averages the token vectors, counting only real tokens, then scales to unit length.
    /// </summary>
    /// <remarks>
    /// Mean pooling over the attention mask, not the first token. Sentence-transformer models
    /// are trained with a mean-pooling head, and taking <c>[CLS]</c> instead is the classic
    /// way to get a model that appears to work and quietly ranks everything slightly wrong.
    /// Normalising here means a cosine similarity is a plain dot product everywhere else.
    /// </remarks>
    private static float[] Pool(Tensor<float> hidden, DenseTensor<long> attention, int row, int length)
    {
        var pooled = new float[VectorSize];
        int counted = 0;

        for (int t = 0; t < length; t++)
        {
            if (attention[row, t] == 0)
            {
                continue;
            }

            counted++;

            for (int d = 0; d < VectorSize; d++)
            {
                pooled[d] += hidden[row, t, d];
            }
        }

        if (counted == 0)
        {
            return pooled;
        }

        double sumOfSquares = 0;

        for (int d = 0; d < VectorSize; d++)
        {
            pooled[d] /= counted;
            sumOfSquares += pooled[d] * (double)pooled[d];
        }

        double magnitude = Math.Sqrt(sumOfSquares);

        if (magnitude > 1e-9)
        {
            for (int d = 0; d < VectorSize; d++)
            {
                pooled[d] = (float)(pooled[d] / magnitude);
            }
        }

        return pooled;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session?.Dispose();
    }
}
