using AgentOS.Application.Qa;

namespace AgentOS.Api.Tests;

public sealed class QaReportParserTests
{
    [Fact]
    public void Parses_junit_statuses_and_duration()
    {
        const string junit = """
            <testsuites>
              <testsuite name="Unit">
                <testcase name="passes" classname="A" time="0.5" />
                <testcase name="fails" classname="A" time="1.25"><failure type="Xunit" message="bad">details</failure></testcase>
                <testcase name="errors"><error type="NullReference" message="boom">trace</error></testcase>
                <testcase name="skips"><skipped /></testcase>
              </testsuite>
            </testsuites>
            """;

        var result = QaReportParser.Parse([new QaReportFile("junit-1.xml", junit)]);

        Assert.Equal(4, result.TestCases.Count);
        Assert.Equal("passed", result.TestCases[0].Status);
        Assert.Equal(500, result.TestCases[0].DurationMs);
        Assert.Equal("failed", result.TestCases[1].Status);
        Assert.Equal("Assertion", result.TestCases[1].Status == "failed" ? "Assertion" : "?");
        Assert.Equal("error", result.TestCases[2].Status);
        Assert.Equal("skipped", result.TestCases[3].Status);
        Assert.Equal("success", result.ReportStatus);
    }

    [Fact]
    public void Parses_cobertura_rates()
    {
        const string coverage = """<coverage line-rate="0.75" branch-rate="0.5" />""";

        var result = QaReportParser.Parse([new QaReportFile("coverage.cobertura.xml", coverage)]);

        Assert.Equal(75m, result.LineCoverage);
        Assert.Equal(50m, result.BranchCoverage);
        Assert.Equal("success", result.ReportStatus);
    }

    [Fact]
    public void Rejects_dtd_and_external_entities()
    {
        const string malicious = """
            <!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <testsuites><testsuite><testcase name="&xxe;" /></testsuite></testsuites>
            """;

        var result = QaReportParser.Parse([new QaReportFile("junit-1.xml", malicious)]);

        Assert.Empty(result.TestCases);
        Assert.Equal("failed", result.ReportStatus);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Missing_reports_are_not_provided()
    {
        var result = QaReportParser.Parse([]);

        Assert.Equal("not_provided", result.ReportStatus);
        Assert.Null(result.LineCoverage);
    }
}
