using System.Collections.Generic;

namespace Dignite.Paperbase.Documents;

public class QaResultDto
{
    public string Answer { get; set; } = default!;
    public IList<QaSourceDto> Sources { get; set; } = new List<QaSourceDto>();
    public string ActualMode { get; set; } = default!;
    public bool IsDegraded { get; set; }
}

public class QaSourceDto
{
    public string Text { get; set; } = default!;
    public int? ChunkIndex { get; set; }
}
