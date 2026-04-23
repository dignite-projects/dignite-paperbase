using System.Collections.Generic;
using Dignite.Paperbase.Abstractions.AI;

namespace Dignite.Paperbase.Contracts.Contracts;

public static class ContractFieldSchemas
{
    public static readonly IList<FieldSchema> All = new List<FieldSchema>
    {
        new() { Name = "Title",                 Type = "string",  Description = "契約タイトル（例: 業務委託基本契約書）", Required = true  },
        new() { Name = "ContractNumber",        Type = "string",  Description = "契約番号（例: 2024-001）",               Required = false },
        new() { Name = "PartyAName",            Type = "string",  Description = "甲（委託者）の名称（例: 株式会社ABC）",   Required = true  },
        new() { Name = "PartyBName",            Type = "string",  Description = "乙（受託者）の名称（例: 株式会社XYZ）",   Required = true  },
        new() { Name = "CounterpartyName",      Type = "string",  Description = "相手方名（甲乙不明の場合の補完）",         Required = false },
        new() { Name = "SignedDate",            Type = "date",    Description = "契約締結日（ISO 8601: yyyy-MM-dd）",       Required = true  },
        new() { Name = "EffectiveDate",         Type = "date",    Description = "契約開始日（ISO 8601: yyyy-MM-dd）",       Required = false },
        new() { Name = "ExpirationDate",        Type = "date",    Description = "契約終了日（ISO 8601: yyyy-MM-dd）",       Required = true  },
        new() { Name = "TotalAmount",           Type = "decimal", Description = "契約金額（数値のみ、単位・カンマ不要）",    Required = false },
        new() { Name = "Currency",              Type = "string",  Description = "通貨コード（例: JPY）",                    Required = false },
        new() { Name = "AutoRenewal",           Type = "boolean", Description = "自動更新の有無（true / false）",            Required = false },
        new() { Name = "TerminationNoticeDays", Type = "integer", Description = "解除通知期間（日数、整数）",                Required = false },
        new() { Name = "GoverningLaw",          Type = "string",  Description = "準拠法（例: 日本法）",                     Required = false },
        new() { Name = "Summary",               Type = "string",  Description = "契約概要（一文程度）",                     Required = false },
    };
}
