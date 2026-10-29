using System.Collections;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MoveItAutomationUI.Services.MoveIt;

// Explicit endpoint contracts keep metadata-only and native-editor entry points
// under the same policy as the object CRUD workflows. Read-only Information remains accessible.
[AttributeUsage(AttributeTargets.Method)]
public sealed class MoveItObjectPermissionAttribute(string type, string operation, string ids = "") : Attribute
{
    public string Type { get; } = type;
    public string Operation { get; } = operation;
    public string Ids { get; } = ids;
}

public sealed class MoveItObjectPermissionFilter(IMoveItObjectAccessService access) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var rule = (context.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo
            .GetCustomAttributes(typeof(MoveItObjectPermissionAttribute), true).OfType<MoveItObjectPermissionAttribute>().FirstOrDefault();
        if (rule is null) { await next(); return; }
        var ct = context.HttpContext.RequestAborted;
        try
        {
            var arguments = context.ActionArguments.Select(pair => (object?)pair).ToArray();
            var type = rule.Type;
            if (type == "KeyImport") type = Values(arguments, "Type").FirstOrDefault()?.ToUpperInvariant() ?? "";
            var ids = rule.Ids.Split(',', StringSplitOptions.RemoveEmptyEntries).SelectMany(key => Values(arguments, key))
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var operation = rule.Operation;
            if (operation == "Open") operation = ids.Length == 0 ? "Import" : "Read";
            if (operation == "Save") operation = ids.Length == 0 ? "Create" : "Edit";
            if (operation is "Create" or "Import" or "Clone")
            {
                var creation = await access.GetCreationAsync(true, ct);
                if (!creation.CanCreate(type)) throw new MoveItObjectAccessException($"Your MOVEit account cannot create {type} objects.");
                if (operation == "Create")
                    await access.RequireCreationAsync(type, Values(arguments, "ResourceGroup").FirstOrDefault(), ct);
                if (operation == "Clone")
                    foreach (var id in ids)
                    {
                        var source = await access.GetObjectAsync(type, id, ct);
                        if (!source.CanEdit)
                            throw new MoveItObjectAccessException("Your MOVEit account cannot edit the source object.");
                    }
            }
            else
            {
                // A malformed empty request must never turn a per-object check into a global grant.
                if (ids.Length == 0) throw new ArgumentException("Select an object before performing this operation.");
                foreach (var id in ids) await access.RequireAsync(type, id, operation, ct);
            }
        }
        catch (MoveItObjectAccessException ex)
        { context.Result = new ObjectResult(new { message = ex.Message, permissionDenied = true, moveItSaved = false }) { StatusCode = 403 }; }
        catch (MoveItAuthenticationException ex)
        { context.Result = new UnauthorizedObjectResult(new { message = ex.Message }); }
        catch (Exception ex) when (ex is MoveItApiException or ArgumentException or InvalidOperationException)
        { context.Result = new BadRequestObjectResult(new { message = ex.Message, moveItSaved = false }); }
        if (context.Result is null) await next();
    }

    private static IEnumerable<string> Values(IEnumerable<object?> arguments, string name)
    {
        foreach (var argument in arguments)
        {
            if (argument is null) continue;
            object? value;
            if (argument is KeyValuePair<string, object?> pair)
            {
                if (!pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                { foreach (var nested in Values([pair.Value], name)) yield return nested; continue; }
                value = pair.Value;
            }
            else if (argument is JsonObject json) value = MoveItObjectAccessService.Node(json, name);
            else value = argument.GetType().GetProperties().FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.GetValue(argument);
            if (value is null) continue;
            if (value is IEnumerable sequence && value is not string)
            { foreach (var item in sequence) if (item is not null) yield return item.ToString() ?? ""; }
            else yield return value.ToString() ?? "";
        }
    }
}
