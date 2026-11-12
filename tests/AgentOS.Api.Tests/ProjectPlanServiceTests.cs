using AgentOS.Api.ProjectPlanning;

namespace AgentOS.Api.Tests;

public sealed class ProjectPlanServiceTests
{
    [Fact]
    public void Parses_valid_tasks_with_epic_and_kind()
    {
        const string json = """
            {
              "recommendedOption": "A",
              "tasks": [
                {"kind":"backend","epic":"M1","title":"Add API","instruction":"Implement the API and tests."},
                {"kind":"frontend","epic":"M2","title":"Add UI","instruction":"Implement the Vue page."}
              ]
            }
            """;

        var tasks = ProjectPlanService.ParseTasks(json);

        Assert.Equal(2, tasks.Count);
        Assert.Equal("backend", tasks[0].Kind);
        Assert.Equal("M1", tasks[0].Epic);
        Assert.Equal("M2", tasks[1].Epic);
    }

    [Fact]
    public void Rejects_unknown_kind_and_missing_instruction()
    {
        const string json = """
            {
              "tasks": [
                {"kind":"unknown","title":"Bad","instruction":"This should be rejected."},
                {"kind":"docs","title":"No instruction"}
              ]
            }
            """;

        Assert.Empty(ProjectPlanService.ParseTasks(json));
    }
}
