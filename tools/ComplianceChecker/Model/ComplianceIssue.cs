using System;

namespace KSPGMAT.Tools.ComplianceChecker.Model {
    public enum IssueSeverity {
        INFO,
        WARN,
        ERROR
    }

    public class ComplianceIssue {
        public IssueSeverity Severity { get; set; }
        public string Category { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; }
        public string Snippet { get; set; }
        public string Suggestion { get; set; }
    }
}
