using System.Threading;
using System.Threading.Tasks;
using Herds;
using RimBridgeServer.Sdk;
using Verse;

namespace Wildlife.BridgeTools;

public sealed class WildlifeBridgeTools
{
    [Tool("wildlife/run-test-suite",
        Title = "Run Wildlife in-game test suite",
        Description = "Runs the Wildlife gameplay assertion suite on RimWorld's main thread and writes its stable report.",
        ResultDescription = "Returns the suite status and the RimWorld save-data report path.",
        Tags = new[] { "wildlife", "testing", "destructive" },
        RequiresAuth = true)]
    [ToolResponse("success", "boolean", "Whether every suite assertion passed.", Always = true)]
    [ToolResponse("status", "string", "PASS or FAIL.", Always = true)]
    [ToolResponse("reportPath", "string", "Absolute path to Wildlife-InGame-Test.txt.", Always = true)]
    public async Task<WildlifeTestToolResult> RunTestSuite(
        IRimBridgeContext context,
        CancellationToken cancellationToken)
    {
        return await context.MainThread.InvokeAsync(() =>
        {
            bool success = WildlifeInGameTestSuite.Run(quiet: true);
            Map map = Find.CurrentMap;
            return new WildlifeTestToolResult
            {
                success = success,
                status = success ? "PASS" : "FAIL",
                suite = "WildlifeInGameTestSuite",
                reportPath = WildlifeInGameTestSuite.ReportPath,
                mapId = map?.uniqueID ?? -1,
                operationId = context.OperationId,
                capabilityId = context.CapabilityId,
                message = success
                    ? "Wildlife in-game test suite passed."
                    : "Wildlife in-game test suite failed; inspect the report for assertion details."
            };
        }, cancellationToken);
    }
}

public sealed class WildlifeTestToolResult
{
    public bool success { get; set; }
    public string status { get; set; }
    public string suite { get; set; }
    public string reportPath { get; set; }
    public string message { get; set; }
    public string operationId { get; set; }
    public string capabilityId { get; set; }
    public int mapId { get; set; }
}
