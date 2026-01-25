namespace EasyDotnet.TestRunner.Models;

public record TestRunnerStatus(
    bool IsLoading,
    OverallStatusEnum OverallStatus,
    int TotalPassed,
    int TotalFailed,
    int TotalSkipped
);