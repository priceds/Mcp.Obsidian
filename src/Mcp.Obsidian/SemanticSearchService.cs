using System.Collections.Concurrent;
using System.Numerics.Tensors;
using System.Text;
using Microsoft.ML.OnnxRuntime;

namespace Mcp.Obsidian;

internal sealed class SemanticSearchService : IDisposable
{
    private static readonly string[] PreferredModelNames =
    [
        "model_q4f16.onnx",
        "model_q4.onnx",
        "model_fp16.onnx",
        "model.onnx",
    ];

    private readonly SemanticSearchSettings _settings;
    private readonly Lock _lock = new();
    private InferenceSession? _session;
    private BertWordPieceTokenizer? _tokenizer;
    private bool _initializationAttempted;

    public SemanticSearchService(SemanticSearchSettings settings)
    {
        _settings = settings;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ModelDirectory);

    public Task<float[]?> ScoreTextsAsync(string query, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return Task.FromResult<float[]?>(null);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();

            var queryEmbedding = EncodeEmbedding(query);
            var scores = new float[texts.Count];

            Parallel.ForEach(Enumerable.Range(0, texts.Count), new ParallelOptions { CancellationToken = cancellationToken }, index =>
            {
                var embedding = EncodeEmbedding(texts[index]);
                scores[index] = CosineSimilarity(queryEmbedding, embedding);
            });

            return (float[]?)scores;
        }, cancellationToken);
    }

    public void Dispose()
    {
        _session?.Dispose();
    }

    private void EnsureInitialized()
    {
        if (_session is not null && _tokenizer is not null)
        {
            return;
        }

        lock (_lock)
        {
            if (_session is not null && _tokenizer is not null)
            {
                return;
            }

            if (_initializationAttempted)
            {
                throw new InvalidOperationException("Semantic search model initialization failed earlier. Fix the configured model path and restart the server.");
            }

            _initializationAttempted = true;
            var modelDirectory = ResolveExistingDirectory(_settings.ModelDirectory!)
                                 ?? throw new InvalidOperationException($"Semantic search model directory '{_settings.ModelDirectory}' was not found.");
            var modelPath = ResolveModelPath(modelDirectory)
                            ?? throw new InvalidOperationException($"No supported ONNX model file was found in '{modelDirectory}'.");
            var vocabPath = Path.Combine(modelDirectory, "vocab.txt");
            if (!File.Exists(vocabPath))
            {
                throw new InvalidOperationException($"Missing vocab.txt in semantic model directory '{modelDirectory}'.");
            }

            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };

            _session = new InferenceSession(modelPath, sessionOptions);
            _tokenizer = new BertWordPieceTokenizer(vocabPath, Math.Clamp(_settings.MaxSequenceLength, 32, 512));
        }
    }

    private float[] EncodeEmbedding(string text)
    {
        var session = _session ?? throw new InvalidOperationException("Semantic model session is not initialized.");
        var tokenizer = _tokenizer ?? throw new InvalidOperationException("Semantic tokenizer is not initialized.");
        var encoded = tokenizer.Encode(text);
        using var inputIds = OrtValue.CreateTensorValueFromMemory(encoded.InputIds, [1, encoded.InputIds.Length]);
        using var attentionMask = OrtValue.CreateTensorValueFromMemory(encoded.AttentionMask, [1, encoded.AttentionMask.Length]);
        using var tokenTypeIds = OrtValue.CreateTensorValueFromMemory(encoded.TokenTypeIds, [1, encoded.TokenTypeIds.Length]);

        var inputs = new Dictionary<string, OrtValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["input_ids"] = inputIds,
            ["attention_mask"] = attentionMask,
        };

        if (session.InputMetadata.ContainsKey("token_type_ids"))
        {
            inputs["token_type_ids"] = tokenTypeIds;
        }

        using var results = session.Run(new RunOptions(), inputs, session.OutputNames);
        if (results.Count == 0)
        {
            throw new InvalidOperationException("Semantic model returned no outputs.");
        }

        var preferredIndex = session.OutputNames
            .Select((name, index) => new { name, index })
            .FirstOrDefault(static item => string.Equals(item.name, "sentence_embedding", StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;
        var output = results[preferredIndex];
        var tensor = output.GetTensorDataAsSpan<float>();
        var dimensions = output.GetTensorTypeAndShape().Shape;

        return dimensions.Length switch
        {
            2 => NormalizeVector(tensor.ToArray()),
            3 => MeanPool(tensor, dimensions, encoded.AttentionMask),
            _ => throw new InvalidOperationException($"Unsupported semantic model output rank {dimensions.Length}."),
        };
    }

    private static float[] MeanPool(ReadOnlySpan<float> values, IReadOnlyList<long> dimensions, IReadOnlyList<long> attentionMask)
    {
        var sequenceLength = checked((int)dimensions[1]);
        var hiddenSize = checked((int)dimensions[2]);
        var pooled = new float[hiddenSize];
        var validTokenCount = 0;

        for (var tokenIndex = 0; tokenIndex < sequenceLength && tokenIndex < attentionMask.Count; tokenIndex++)
        {
            if (attentionMask[tokenIndex] == 0)
            {
                continue;
            }

            validTokenCount++;
            var offset = tokenIndex * hiddenSize;
            for (var hiddenIndex = 0; hiddenIndex < hiddenSize; hiddenIndex++)
            {
                pooled[hiddenIndex] += values[offset + hiddenIndex];
            }
        }

        if (validTokenCount == 0)
        {
            return NormalizeVector(pooled);
        }

        for (var hiddenIndex = 0; hiddenIndex < hiddenSize; hiddenIndex++)
        {
            pooled[hiddenIndex] /= validTokenCount;
        }

        return NormalizeVector(pooled);
    }

    private static float[] NormalizeVector(float[] vector)
    {
        var magnitude = 0d;
        foreach (var value in vector)
        {
            magnitude += value * value;
        }

        if (magnitude <= double.Epsilon)
        {
            return vector;
        }

        var scale = (float)(1d / Math.Sqrt(magnitude));
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] *= scale;
        }

        return vector;
    }

    private static float CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var limit = Math.Min(left.Count, right.Count);
        var dot = 0f;
        for (var index = 0; index < limit; index++)
        {
            dot += left[index] * right[index];
        }

        return Math.Clamp((dot + 1f) / 2f, 0f, 1f);
    }

    private static string? ResolveExistingDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Directory.Exists(expanded) ? Path.GetFullPath(expanded) : null;
    }

    private static string? ResolveModelPath(string modelDirectory)
    {
        foreach (var fileName in PreferredModelNames)
        {
            var directPath = Path.Combine(modelDirectory, fileName);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            var nestedPath = Path.Combine(modelDirectory, "onnx", fileName);
            if (File.Exists(nestedPath))
            {
                return nestedPath;
            }
        }

        return null;
    }

    private sealed class BertWordPieceTokenizer
    {
        private readonly Dictionary<string, int> _vocabulary;
        private readonly int _maxSequenceLength;
        private readonly int _clsId;
        private readonly int _sepId;
        private readonly int _padId;
        private readonly int _unkId;

        public BertWordPieceTokenizer(string vocabularyPath, int maxSequenceLength)
        {
            _vocabulary = File.ReadAllLines(vocabularyPath)
                .Select((token, index) => new KeyValuePair<string, int>(token.Trim(), index))
                .Where(static entry => entry.Key.Length > 0)
                .ToDictionary(StringComparer.Ordinal);
            _maxSequenceLength = maxSequenceLength;
            _clsId = GetRequiredTokenId("[CLS]");
            _sepId = GetRequiredTokenId("[SEP]");
            _padId = GetRequiredTokenId("[PAD]");
            _unkId = GetRequiredTokenId("[UNK]");
        }

        public EncodedText Encode(string text)
        {
            var basicTokens = BasicTokenize(text);
            var tokenIds = new List<long>(_maxSequenceLength) { _clsId };

            foreach (var token in basicTokens)
            {
                foreach (var pieceId in TokenizeWordPiece(token))
                {
                    if (tokenIds.Count >= _maxSequenceLength - 1)
                    {
                        break;
                    }

                    tokenIds.Add(pieceId);
                }

                if (tokenIds.Count >= _maxSequenceLength - 1)
                {
                    break;
                }
            }

            tokenIds.Add(_sepId);
            var attentionMask = Enumerable.Repeat(1L, tokenIds.Count).ToList();
            var tokenTypeIds = Enumerable.Repeat(0L, tokenIds.Count).ToList();

            while (tokenIds.Count < _maxSequenceLength)
            {
                tokenIds.Add(_padId);
                attentionMask.Add(0);
                tokenTypeIds.Add(0);
            }

            return new EncodedText(tokenIds.ToArray(), attentionMask.ToArray(), tokenTypeIds.ToArray());
        }

        private int GetRequiredTokenId(string token)
        {
            return _vocabulary.TryGetValue(token, out var tokenId)
                ? tokenId
                : throw new InvalidOperationException($"Vocabulary is missing required token '{token}'.");
        }

        private IEnumerable<long> TokenizeWordPiece(string token)
        {
            if (_vocabulary.TryGetValue(token, out var tokenId))
            {
                yield return tokenId;
                yield break;
            }

            var start = 0;
            var tokenPieces = new List<int>();
            while (start < token.Length)
            {
                var end = token.Length;
                int? currentId = null;

                while (start < end)
                {
                    var candidate = token[start..end];
                    if (start > 0)
                    {
                        candidate = $"##{candidate}";
                    }

                    if (_vocabulary.TryGetValue(candidate, out var candidateId))
                    {
                        currentId = candidateId;
                        break;
                    }

                    end--;
                }

                if (currentId is null)
                {
                    yield return _unkId;
                    yield break;
                }

                tokenPieces.Add(currentId.Value);
                start = end;
            }

            foreach (var pieceId in tokenPieces)
            {
                yield return pieceId;
            }
        }

        private static IReadOnlyList<string> BasicTokenize(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            var tokens = new List<string>();
            var current = new StringBuilder();

            foreach (var character in normalized)
            {
                if (char.IsWhiteSpace(character))
                {
                    FlushCurrent(tokens, current);
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    current.Append(character);
                    continue;
                }

                FlushCurrent(tokens, current);
                if (!char.IsControl(character))
                {
                    tokens.Add(character.ToString());
                }
            }

            FlushCurrent(tokens, current);
            return tokens;
        }

        private static void FlushCurrent(List<string> tokens, StringBuilder current)
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    private sealed record EncodedText(long[] InputIds, long[] AttentionMask, long[] TokenTypeIds);
}
