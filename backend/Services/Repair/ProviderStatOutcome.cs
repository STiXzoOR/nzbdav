namespace NzbWebDAV.Services.Repair;

/// <summary>One provider's answer to a STAT for a single segment.</summary>
public readonly record struct ProviderStatOutcome(ProviderStatOutcome.Kind Result)
{
    public enum Kind
    {
        Exists,               // 223 ArticleExists
        DefinitivelyMissing,  // 430 NoArticleWithThatMessageId
        TransientError,       // timeout / connection reset / 400 / 403 / unreachable
    }
}
