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
                {"taskKey":"T1","dependsOn":[],"kind":"backend","epic":"M1","title":"Add API","instruction":"Implement the API and tests."},
                {"taskKey":"T2","dependsOn":["T1"],"kind":"frontend","epic":"M2","title":"Add UI","instruction":"Implement the Vue page."}
              ]
            }
            """;

        var tasks = ProjectPlanService.ParseTasks(json);

        Assert.Equal(2, tasks.Count);
        Assert.Equal("backend", tasks[0].Kind);
        Assert.Equal("M1", tasks[0].Epic);
        Assert.Equal("M2", tasks[1].Epic);
        Assert.Equal("T2", tasks[1].TaskKey);
        Assert.Equal(["T1"], tasks[1].DependsOn);
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

    [Fact]
    public void Rejects_missing_dependency()
    {
        var tasks = new[]
        {
            new ProjectPlanTask("T1", "backend", "M1", "A", "Do A properly.", ["T9"])
        };

        var error = ProjectPlanService.ValidateDependencies(tasks);

        Assert.Contains("missing", error);
    }

    [Fact]
    public void Rejects_dependency_cycle()
    {
        var tasks = new[]
        {
            new ProjectPlanTask("T1", "backend", "M1", "A", "Do A properly.", ["T2"]),
            new ProjectPlanTask("T2", "frontend", "M1", "B", "Do B properly.", ["T1"])
        };

        var error = ProjectPlanService.ValidateDependencies(tasks);

        Assert.Contains("cycle", error);
    }
}
