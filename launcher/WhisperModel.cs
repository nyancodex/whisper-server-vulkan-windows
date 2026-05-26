namespace WhisperLauncher;

public sealed record WhisperModel(string File, long ApproxBytes, string Wer, string Notes)
{
    public string DisplayName => $"{File}  ({ApproxMb} MB, WER {Wer}) - {Notes}";
    public long ApproxMb => ApproxBytes / 1024 / 1024;

    public static readonly WhisperModel[] Choices =
    {
        new("ggml-tiny.bin",                  75L  * 1024 * 1024, "~7-8%", "fastest, English-heavy"),
        new("ggml-base.bin",                  142L * 1024 * 1024, "~5%",   "good tradeoff"),
        new("ggml-small.bin",                 466L * 1024 * 1024, "~3.4%", "balanced classic"),
        new("ggml-large-v3-turbo-q5_0.bin",   547L * 1024 * 1024, "~2%",   "multilingual, recommended"),
    };

    public const string DefaultFile = "ggml-large-v3-turbo-q5_0.bin";

    public string DownloadUrl =>
        $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{File}";
}
