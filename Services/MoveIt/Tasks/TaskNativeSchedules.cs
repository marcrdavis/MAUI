using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

// Common native schedule transport for Traditional and Sync; legacy APIs delegate here.
internal static class TaskNativeSchedules
{
    // A value editor cannot rebuild unknown native Schedule attributes from a REST projection.
    // The supplied subtree is derived from its revision-checked native read; retain all unrelated XML.
    public static Task<MoveItTaskConfiguration> UpdateDefinitionAsync(IMoveItComClient com, string taskId, string schedulesXml, CancellationToken ct)
    {
        var schedules = XElement.Parse(schedulesXml);
        if (schedules.Name.LocalName != "Schedules") throw new ArgumentException("A native Schedules definition is required.");
        return ObjectCheckout.ExecuteNativeAsync("Task", taskId, token => com.ModifyTaskConfigurationAsync(taskId, xml =>
        {
            ObjectCheckout.VerifyNativeLifetimeRevision("Task", taskId, xml);
            ObjectEditVersion.VerifyNativeSchedules(xml);
            var root = XElement.Parse(xml);
            if (root.Element("Schedules") is { } current) current.ReplaceWith(new XElement(schedules));
            else root.Add(new XElement(schedules));
            return root.ToString(SaveOptions.DisableFormatting);
        }, token), saved => saved.Xml, ct);
    }

    public static Task<MoveItTaskConfiguration> UpdateAsync(IMoveItComClient com, string taskId, JsonArray patch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        ArgumentNullException.ThrowIfNull(patch);
        return ObjectCheckout.ExecuteNativeAsync("Task", taskId.Trim(), ct => com.ModifyTaskConfigurationAsync(taskId.Trim(), xml =>
        {
            ObjectCheckout.VerifyNativeLifetimeRevision("Task", taskId.Trim(), xml);
            ObjectEditVersion.VerifyNativeSchedules(xml);
            return TraditionalTaskScheduleXml.ApplySchedules(xml, patch);
        }, ct), saved => saved.Xml, cancellationToken);
    }
}
