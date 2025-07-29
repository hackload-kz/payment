namespace PaymentGateway.Core.Models;

// Base compliance report classes
public class ComplianceReport
{
    public Guid ReportId { get; set; } = Guid.NewGuid();
    public string ReportName { get; set; } = string.Empty;
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string? RequestedBy { get; set; }
    public int TotalEntries { get; set; }
    public int CriticalIssues { get; set; }
    public int SecurityEvents { get; set; }
    public double ComplianceScore { get; set; }
    public Dictionary<string, object> Sections { get; set; } = new();
}

public class ComplianceReportSummary
{
    public Guid ReportId { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

// PCI DSS Compliance Report
public class PciDssComplianceReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string ComplianceFramework { get; set; } = string.Empty;
    public double OverallComplianceScore { get; set; }
    public bool IsCompliant { get; set; }
    public double Requirement10Score { get; set; }
    public double Requirement8Score { get; set; }
    public double Requirement7Score { get; set; }
    public Dictionary<string, string> Requirements { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

// GDPR Compliance Report
public class GdprComplianceReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string ComplianceFramework { get; set; } = string.Empty;
    public double OverallComplianceScore { get; set; }
    public bool IsCompliant { get; set; }
    public double Article5Score { get; set; }
    public double Article32Score { get; set; }
    public double Article33Score { get; set; }
    public double Article30Score { get; set; }
    public double DataSubjectRightsScore { get; set; }
    public Dictionary<string, string> Articles { get; set; } = new();
    public string DataSubjectRights { get; set; } = string.Empty;
    public List<string> Recommendations { get; set; } = new();
}

// SOX Compliance Report
public class SoxComplianceReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string ComplianceFramework { get; set; } = string.Empty;
    public double OverallComplianceScore { get; set; }
    public bool IsCompliant { get; set; }
    public double Section404Score { get; set; }
    public double DataIntegrityScore { get; set; }
    public double ChangeManagementScore { get; set; }
    public double SegregationScore { get; set; }
    public Dictionary<string, string> Sections { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

// Audit Trail Report
public class AuditTrailReport
{
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int TotalEntries { get; set; }
    public DateTime? FirstEntry { get; set; }
    public DateTime? LastEntry { get; set; }
    public Dictionary<string, int> EntriesByAction { get; set; } = new();
    public Dictionary<string, int> EntriesByUser { get; set; } = new();
    public List<string> SuspiciousPatterns { get; set; } = new();
    public bool IntegrityVerified { get; set; }
    public int IntegrityIssues { get; set; }
}

// Data Processing Report
public class DataProcessingReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public string? DataSubject { get; set; }
    public DateTime GeneratedAt { get; set; }
    public Dictionary<string, int> ProcessingActivities { get; set; } = new();
    public Dictionary<string, int> DataCategoriesProcessed { get; set; } = new();
    public Dictionary<string, int> ProcessingPurposes { get; set; } = new();
    public Dictionary<string, int> LegalBasisCount { get; set; } = new();
    public Dictionary<string, string> DataRetentionCompliance { get; set; } = new();
}

// Data Retention Report
public class DataRetentionReport
{
    public DateTime GeneratedAt { get; set; }
    public Dictionary<string, int> RetentionPolicies { get; set; } = new();
    public Dictionary<string, DataRetentionStatus> RetentionCompliance { get; set; } = new();
    public bool OverallCompliance { get; set; }
}

public class DataRetentionStatus
{
    public string DataType { get; set; } = string.Empty;
    public int RetentionPeriodDays { get; set; }
    public int EntriesRequiringArchival { get; set; }
    public DateTime? OldestEntry { get; set; }
    public string ComplianceStatus { get; set; } = string.Empty;
}

// Data Breach Report
public class DataBreachReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public List<DataBreachIncident> Incidents { get; set; } = new();
    public Dictionary<string, int> IncidentsBySeverity { get; set; } = new();
    public double AverageResponseTime { get; set; }
    public string BreachNotificationCompliance { get; set; } = string.Empty;
}

public class DataBreachIncident
{
    public Guid IncidentId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] AffectedDataTypes { get; set; } = Array.Empty<string>();
    public string? IpAddress { get; set; }
    public string? UserId { get; set; }
    public string Status { get; set; } = string.Empty;
}

// Access Report
public class AccessReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int SuccessfulAttempts { get; set; }
    public int FailedAttempts { get; set; }
    public Dictionary<int, int> AccessByHour { get; set; } = new();
    public Dictionary<string, int> AccessByIP { get; set; } = new();
    public List<string> SuspiciousPatterns { get; set; } = new();
}

// Privileged Access Report
public class PrivilegedAccessReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public Dictionary<string, int> PrivilegedOperationsByUser { get; set; } = new();
    public Dictionary<string, int> PrivilegedOperationsByType { get; set; } = new();
    public int AdministrativeChanges { get; set; }
    public int OffHoursPrivilegedAccess { get; set; }
}

// Financial Audit Report
public class FinancialAuditReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public Dictionary<string, int> PaymentTransactions { get; set; } = new();
    public int FailedTransactions { get; set; }
    public int HighValueTransactions { get; set; }
    public double TransactionIntegrityScore { get; set; }
}

// Transaction Audit Report
public class TransactionAuditReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public string? TeamSlug { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int TotalTransactions { get; set; }
    public Dictionary<string, int> TransactionsByStatus { get; set; } = new();
    public Dictionary<DateTime, int> DailyTransactions { get; set; } = new();
    public double? AverageProcessingTime { get; set; }
    public double? MaxProcessingTime { get; set; }
    public int ExceptionTransactions { get; set; }
}