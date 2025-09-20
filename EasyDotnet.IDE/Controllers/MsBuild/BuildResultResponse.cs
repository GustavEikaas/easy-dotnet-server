using System.Collections.Generic;
using EasyDotnet.Services;

namespace EasyDotnet.Controllers.MsBuild;

public sealed record BuildResultResponse(bool Success, IAsyncEnumerable<BuildMessage> Errors, IAsyncEnumerable<BuildMessage> Warnings);