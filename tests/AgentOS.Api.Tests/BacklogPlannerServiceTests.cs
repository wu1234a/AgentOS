using AgentOS.Api.Backlog;

namespace AgentOS.Api.Tests;

public sealed class BacklogPlannerServiceTests
{
    [Fact]
    public void Parses_a_fenced_json_array_and_ignores_unknown_kinds()
    {
        const string content = """
            Here are the tasks:
            ```json
            [
              {"kind":"backend","title":"Add inventory API","instruction":"Implement the inventory query endpoint and tests."},
              {"kind":"frontend","title":"Add inventory page","instruction":"Add the Vue inventory page and route."},
              {"kind":"unknown","title":"Ignore me","instruction":"This should not become a task."}
            ]
            ```
            """;

        var tasks = BacklogPlannerService.ParseTasks(content, 5);

        Assert.Equal(2, tasks.Count);
        Assert.Equal("backend", tasks[0].Kind);
        Assert.Equal("frontend", tasks[1].Kind);
    }

    [Fact]
    public void Respects_the_maximum_item_count()
    {
        const string content = """
            [
              {"kind":"docs","title":"Doc one","instruction":"Create the first documentation file."},
              {"kind":"docs","title":"Doc two","instruction":"Create the second documentation file."}
            ]
            """;

        var tasks = BacklogPlannerService.ParseTasks(content, 1);

        Assert.Single(tasks);
        Assert.Equal("Doc one", tasks[0].Title);
    }
}
