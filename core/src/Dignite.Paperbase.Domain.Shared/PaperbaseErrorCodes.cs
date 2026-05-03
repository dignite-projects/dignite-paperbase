namespace Dignite.Paperbase;

public static class PaperbaseErrorCodes
{
    public const string MarkdownIsImmutable = "Paperbase:MarkdownIsImmutable";
    public const string DocumentRelationDocumentIdRequired = "Paperbase:DocumentRelationDocumentIdRequired";
    public const string DocumentRelationCannotTargetSelf = "Paperbase:DocumentRelationCannotTargetSelf";
    public const string DocumentRelationConfidenceOutOfRange = "Paperbase:DocumentRelationConfidenceOutOfRange";
    public const string InvalidDocumentTypeCode = "Paperbase:InvalidDocumentTypeCode";
    public const string DocumentDuplicate = "Paperbase:DocumentDuplicate";
    public const string DocumentInRecycleBin = "Paperbase:DocumentInRecycleBin";
    public const string DuplicateClientTurnId = "Paperbase:DuplicateClientTurnId";
    public const string ChatConversationScopeConflict = "Paperbase:ChatConversationScopeConflict";
}
