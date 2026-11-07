using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using AgentOS.Domain.Qa;

namespace AgentOS.Application.Qa;

public sealed record QaReportFile(string Name, string Content);

public sealed record QaReportParseResult(
    IReadOnlyList<QaTestCase> TestCases,
    decimal? LineCoverage,
    decimal? BranchCoverage,
    string ReportStatus,
    IReadOnlyList<string> Issues);

/// <summary>
/// Deterministic, offline JUnit/Cobertura parsing with DTD and external entity resolution disabled.
/// </summary>
public static class QaReportParser
{
    private const int MaxJunitFiles = 20;
    private const int MaxTestCases = 5000;
    private const int MaxJunitBytes = 2 * 1024 * 1024;
    private const int MaxCoverageBytes = 4 * 1024 * 1024;

    public static QaReportParseResult Parse(IReadOnlyList<QaReportFile> reports)
    {
        var cases = new List<QaTestCase>();
        var issues = new List<string>();
        decimal? lineCoverage = null;
        decimal? branchCoverage = null;
        var junitFiles = 0;
        var validFiles = 0;
        var providedFiles = 0;

        foreach (var report in reports)
        {
            var limit = report.Name.StartsWith("junit-", StringComparison.Ordinal)
                ? MaxJunitBytes
                : MaxCoverageBytes;

            if (report.Content.Length > limit)
            {
                issues.Add($"{report.Name}: report exceeds {(limit / (1024 * 1024))} MiB.");
                continue;
            }

            providedFiles++;

            if (report.Name.StartsWith("junit-", StringComparison.Ordinal))
            {
                junitFiles++;
                if (junitFiles > MaxJunitFiles)
                {
                    issues.Add("junit: more than 20 reports.");
                    break;
                }

                if (TryParseJunit(report, limit, cases, out var error))
                {
                    validFiles++;
                }
                else
                {
                    issues.Add($"{report.Name}: {error}");
                }

                continue;
            }

            if (string.Equals(report.Name, "coverage.cobertura.xml", StringComparison.Ordinal))
            {
                if (TryParseCobertura(report.Content, limit, out var line, out var branch, out var error))
                {
                    lineCoverage = line ?? lineCoverage;
                    branchCoverage = branch ?? branchCoverage;
                    validFiles++;
                }
                else
                {
                    issues.Add($"{report.Name}: {error}");
                }
            }
        }

        if (cases.Count > MaxTestCases)
        {
            cases = cases.Take(MaxTestCases).ToList();
            issues.Add($"junit: more than {MaxTestCases} test cases; truncated.");
        }

        var status = providedFiles == 0
            ? "not_provided"
            : validFiles == 0
                ? "failed"
                : issues.Count > 0
                    ? "partial"
                    : "success";

        return new QaReportParseResult(cases, lineCoverage, branchCoverage, status, issues);
    }

    private static bool TryParseJunit(QaReportFile report, int maxChars, List<QaTestCase> cases, out string? error)
    {
        error = null;

        try
        {
            using var reader = XmlReader.Create(
                new StringReader(report.Content),
                SecureSettings(maxChars));
            var document = XDocument.Load(reader);

            foreach (var testCase in document.Descendants().Where(element => element.Name.LocalName == "testcase"))
            {
                var suite = testCase.Ancestors().FirstOrDefault(element => element.Name.LocalName == "testsuite");
                var failure = testCase.Elements().FirstOrDefault(element => element.Name.LocalName is "failure" or "error");
                var skipped = testCase.Elements().Any(element => element.Name.LocalName == "skipped");
                var status = failure is null
                    ? skipped ? "skipped" : "passed"
                    : failure.Name.LocalName == "error" ? "error" : "failed";

                var duration = TryGetDouble(testCase.Attribute("time")?.Value)
                    is { } seconds
                    ? (int?)Math.Round(seconds * 1000)
                    : null;

                cases.Add(new QaTestCase
                {
                    Id = Guid.CreateVersion7(),
                    Ordinal = cases.Count,
                    ReportPath = report.Name,
                    SuiteName = Truncate(suite?.Attribute("name")?.Value, 512),
                    ClassName = Truncate(testCase.Attribute("classname")?.Value, 512),
                    Name = Truncate(testCase.Attribute("name")?.Value ?? "(unnamed)", 512) ?? "(unnamed)",
                    Status = status,
                    DurationMs = duration,
                    FailureType = Truncate(failure?.Attribute("type")?.Value, 512),
                    FailureMessage = Truncate(failure?.Attribute("message")?.Value, 2000),
                    FailureDetail = Truncate(failure?.Value, 20000)
                });
            }

            return true;
        }
        catch (XmlException)
        {
            error = "invalid XML.";
            return false;
        }
    }

    private static bool TryParseCobertura(
        string content,
        int maxChars,
        out decimal? lineCoverage,
        out decimal? branchCoverage,
        out string? error)
    {
        lineCoverage = null;
        branchCoverage = null;
        error = null;

        try
        {
            using var reader = XmlReader.Create(new StringReader(content), SecureSettings(maxChars));
            var document = XDocument.Load(reader);
            var root = document.Root;

            if (root is null || root.Name.LocalName != "coverage")
            {
                error = "root element is not coverage.";
                return false;
            }

            lineCoverage = ParseRate(root.Attribute("line-rate")?.Value);
            branchCoverage = ParseRate(root.Attribute("branch-rate")?.Value);
            return true;
        }
        catch (XmlException)
        {
            error = "invalid XML.";
            return false;
        }
    }

    private static XmlReaderSettings SecureSettings(int maxChars) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        MaxCharactersInDocument = maxChars
    };

    private static decimal? ParseRate(string? value)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)
            && rate is >= 0 and <= 1)
        {
            return Math.Round(rate * 100, 2);
        }

        return null;
    }

    private static double? TryGetDouble(string? value)
        => double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}
