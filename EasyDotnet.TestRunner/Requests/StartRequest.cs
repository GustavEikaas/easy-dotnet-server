namespace EasyDotnet.TestRunner.Requests;

/// <summary>
/// Request to start test discovery for a given solution.
/// </summary>
/// <param name="SolutionFilePath">The full path to the solution file to discover tests from.</param>
public sealed record StartRequest(string SolutionFilePath);
