// Shared COS topology, sanitized editor projection and captured nested v0 composition.
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;

public static class AdvancedTaskDefinition
{
    public static readonly string[] Kinds = ["Source", "Process", "Destination", "For", "If", "Email", "RunTask", "UpdOrig", "Comment"];
    public static IEnumerable<JsonObject> Flatten(JsonNode? steps)
    {
        if (steps is not JsonArray array) yield break;
        foreach (var wrapper in array.OfType<JsonObject>())
        {
            yield return wrapper;
            var kind = Kind(wrapper); var body = wrapper[kind] as JsonObject;
            if (body is null) continue;
            if (kind == "For") foreach (var child in Flatten(body["steps"])) yield return child;
            if (kind == "If")
            {
                foreach (var branch in (body["When"] as JsonArray ?? []).OfType<JsonObject>())
                    foreach (var child in Flatten(branch["steps"])) yield return child;
                foreach (var child in Flatten(body["Otherwise"]?["steps"])) yield return child;
            }
        }
    }
    public static string Kind(JsonObject wrapper) => wrapper.FirstOrDefault(p => p.Value is JsonObject).Key ?? "Unsupported";
    public static JsonArray Project(JsonNode? steps)
    {
        var output = new JsonArray();
        foreach (var wrapper in (steps as JsonArray ?? []).OfType<JsonObject>())
        {
            var kind = Kind(wrapper); var body = wrapper[kind]!.AsObject(); var id = GetString(body, "ID");
            var node = new JsonObject { ["key"] = id, ["id"] = id, ["kind"] = kind, ["disabled"] = GetBool(body,"Disabled"), ["fields"] = new JsonObject() };
            var fields = node["fields"]!.AsObject();
            foreach (var key in kind switch { "Comment" => new[]{"Text"}, "UpdOrig" => ["Action","RenameTo","DelRename"], _ => Array.Empty<string>() })
                if(body[key] is { } value) fields[key] = value.DeepClone();
            if(kind == "For") node["steps"] = Project(body["steps"]);
            if(kind == "If")
            {
                node["when"] = new JsonArray((body["When"] as JsonArray ?? []).OfType<JsonObject>().Select((branch,index) => (JsonNode)new JsonObject {
                    ["key"] = id+":when:"+index, ["sourceIndex"] = index, ["criteria"] = branch["Criteria"]?.DeepClone(), ["steps"] = Project(branch["steps"])
                }).ToArray());
                if(body["Otherwise"] is JsonObject otherwise) node["otherwise"] = new JsonObject { ["key"] = id+":otherwise", ["steps"] = Project(otherwise["steps"]) };
            }
            output.Add(node);
        }
        return output;
    }

    public static void Apply(JsonObject task, JsonArray flow, JsonArray? stepPatches, JsonArray? actionPatches, ITraditionalTaskDefinitionComposer composer)
    {
        var original = new Dictionary<string,JsonObject>(StringComparer.Ordinal);
        long max = 0;
        foreach(var wrapper in Flatten(task["steps"]))
        {
            var id = GetString(wrapper[Kind(wrapper)]!.AsObject(),"ID");
            if(id.Length == 0 || !original.TryAdd(id,wrapper)) throw new InvalidOperationException("The Advanced Task contains missing or duplicate step IDs. Review it in MOVEit before editing.");
            if(long.TryParse(id,out var number)) max=Math.Max(max,number);
        }
        var next = Math.Max(max+1,long.TryParse(GetString(task,"NextEID"),out var nextValue)?nextValue:1);
        var allocated = new Dictionary<string,string>(StringComparer.Ordinal);
        IEnumerable<JsonObject> Requested(JsonArray input) {
            foreach(var node in input.OfType<JsonObject>()) {
                yield return node;
                foreach(var child in Requested(node["steps"] as JsonArray ?? []))yield return child;
                foreach(var branch in (node["when"] as JsonArray ?? []).OfType<JsonObject>())foreach(var child in Requested(branch["steps"] as JsonArray ?? []))yield return child;
                if(node["otherwise"] is JsonObject otherwise)foreach(var child in Requested(otherwise["steps"] as JsonArray ?? []))yield return child;
            }
        }
        foreach(var node in Requested(flow).Where(node=>GetString(node,"id").Length==0).OrderBy(node=>GetInt(node,"order",int.MaxValue))) {
            var key=GetString(node,"key");if(key.Length==0||allocated.ContainsKey(key))throw new InvalidOperationException("New Advanced step locations must be unique.");
            allocated[key]=(next++).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var keys = new HashSet<string>(StringComparer.Ordinal); var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var standard = (stepPatches ?? []).OfType<JsonObject>().ToDictionary(p=>GetString(p,"clientKey"),StringComparer.Ordinal);
        var actions = (actionPatches ?? []).OfType<JsonObject>().ToDictionary(p=>GetString(p,"clientKey"),StringComparer.Ordinal);
        JsonArray Build(JsonArray input)
        {
            var output=new JsonArray();
            foreach(var node in input.OfType<JsonObject>())
            {
                var key=GetString(node,"key");var kind=GetString(node,"kind");var requestedId=GetString(node,"id");var cloneId=GetString(node,"cloneFromId");
                if(key.Length==0 || !keys.Add(key)) throw new InvalidOperationException("Advanced flow locations must be unique.");
                var templateId=requestedId.Length>0?requestedId:cloneId;
                JsonObject? template=null;
                if(templateId.Length>0 && (!original.TryGetValue(templateId,out template) || Kind(template)!=kind)) throw new InvalidOperationException("An Advanced step changed or is no longer available. Reopen the Task.");
                var id=requestedId.Length>0?requestedId:allocated[key];
                if(!usedIds.Add(id)) throw new InvalidOperationException("An Advanced step cannot appear in more than one flow position.");
                if(!Kinds.Contains(kind))
                {
                    if(template is null || cloneId.Length>0)throw new InvalidOperationException("An unsupported step can only be preserved or removed.");
                    output.Add(template.DeepClone());continue;
                }
                var wrapper=template?.DeepClone().AsObject() ?? new JsonObject();
                var body=wrapper[kind] as JsonObject ?? new JsonObject();
                body["ID"]=id;body["Disabled"]=GetBool(node,"disabled")?1:0; wrapper[kind]=body.Parent is null?body:body.DeepClone();body=wrapper[kind]!.AsObject();
                if(kind is "Source" or "Process" or "Destination")
                {
                    if(!standard.TryGetValue(key,out var patchItem)) throw new InvalidOperationException("Step properties are missing from the Advanced draft.");
                    var item=patchItem.DeepClone().AsObject();item["id"]=id;item["index"]=-1;
                    item["clone"]=cloneId.Length>0;
                    if(item["patch"] is JsonObject patch) patch["ID"]=id;
                    // An allocated ID does not make a new step an authoritative existing step.
                    // Let the shared composer create it, apply Host defaults and validate new
                    // credentials. Only edits/clones have a server template to preserve.
                    var temporary=new JsonObject{["steps"]=template is null ? new JsonArray() : new JsonArray(wrapper.DeepClone())};
                    composer.ApplyEditorSteps(temporary,new JsonArray(item));
                    wrapper=temporary["steps"]![0]!.DeepClone().AsObject();body=wrapper[kind]!.AsObject();body["Disabled"]=GetBool(node,"disabled")?1:0;
                }
                else if(kind is "Email" or "RunTask")
                {
                    if(!actions.TryGetValue(key,out var action))throw new InvalidOperationException("Email or Run Task properties are missing.");
                    if(kind=="Email")
                    {
                        if(GetString(action,"hostId").Length==0 || GetString(action,"addressTo").Trim().Length==0)throw new InvalidOperationException("Select an SMTP Host and enter Email Recipients.");
                        foreach(var pair in new[]{("HostID","hostId"),("AddressTo","addressTo"),("Subject","subject"),("Message","message")})body[pair.Item1]=GetString(action,pair.Item2);
                        body["IsHTML"]=GetBool(action,"isHtml")?1:0;
                        foreach(var field in new[]{"UseDefRetryCount","UseDefRetryTimeoutSecs","UseDefConnTimeoutSecs","UseDefDataTimeoutSecs"})if(body[field] is null)body[field]=1;
                    }
                    else
                    {
                        var target=GetString(action,"taskId");if(target.Length==0 || target==GetString(task,"ID"))throw new InvalidOperationException("Select another Task to run.");
                        body["TaskID"]=target;body["Wait"]=GetBool(action,"wait")?1:0;
                        if(action["parameters"] is JsonObject parameters && (body["Parameters"] is not null || (parameters["any"] as JsonArray)?.Count>0))body["Parameters"]=MergeParameters(body["Parameters"],parameters,useNativeTaskPutShape:true);
                    }
                }
                else if(kind=="Comment")body["Text"]=GetString(node["fields"] as JsonObject ?? new JsonObject(),"Text");
                else if(kind=="UpdOrig")
                {
                    var fields=node["fields"] as JsonObject ?? new JsonObject();var action=GetString(fields,"Action");
                    if(action is not ("d" or "r"))throw new InvalidOperationException("Select Delete or Rename for Update Original.");
                    if(action=="r" && GetString(fields,"RenameTo").Trim().Length==0)throw new InvalidOperationException("Enter Rename To for Update Original.");
                    body["Action"]=action;body["RenameTo"]=action=="r"?GetString(fields,"RenameTo"):"";body["DelRename"]=action=="r"&&GetBool(fields,"DelRename")?1:0;
                }
                else if(kind=="For")body["steps"]=Build(node["steps"] as JsonArray ?? []);
                else if(kind=="If")
                {
                    var branches=node["when"] as JsonArray ?? [];if(branches.Count==0)throw new InvalidOperationException("A Conditional step requires an If branch.");
                    var previous=body["When"] as JsonArray;var when=new JsonArray();var branchIndex=0;
                    foreach(var branch in branches.OfType<JsonObject>())
                    {
                        var criteria=branch["criteria"] as JsonObject ?? throw new InvalidOperationException("Conditional criteria are required.");
                        // Branch keys carry original index so reordering/deletion preserves unowned branch fields.
                        var branchKey=GetString(branch,"key");var tail=branch["sourceIndex"]?.ToString() ?? branchKey.Split(':').LastOrDefault();
                        var existingBranch=int.TryParse(tail,out var oldIndex)&&previous is not null&&oldIndex>=0&&oldIndex<previous.Count?previous[oldIndex] as JsonObject:null;
                        if (!JsonNode.DeepEquals(criteria, existingBranch?["Criteria"])) ValidateCriteria(criteria);
                        var target=existingBranch?.DeepClone().AsObject() ?? new JsonObject();
                        target["Criteria"]=criteria.DeepClone();target["steps"]=Build(branch["steps"] as JsonArray ?? []);target["__parentStepId"]=id;target["__branchIndex"]=branchIndex++;when.Add(target);
                    }
                    body["When"]=when;
                    if(node["otherwise"] is JsonObject otherwise)
                    {
                        var target=(body["Otherwise"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();target["steps"]=Build(otherwise["steps"] as JsonArray ?? []);target["__parentStepId"]=id;target["__branchIndex"]=branchIndex;body["Otherwise"]=target;
                    }
                    else body.Remove("Otherwise");
                }
                output.Add(wrapper);
            }
            return output;
        }
        task["steps"]=Build(flow);task["NextEID"]=next;
    }
    public static void ValidateCriteria(JsonObject criteria)
    {
        JsonArray comparisons;
        if(criteria["comp"] is JsonObject comp)comparisons=new JsonArray(comp.DeepClone());
        else if((criteria["And"] ?? criteria["Or"]) is JsonObject group && group["comp"] is JsonArray array)comparisons=array;
        else throw new InvalidOperationException("Use a comparison or Match All/Any criteria.");
        if(comparisons.Count==0)throw new InvalidOperationException("Add at least one conditional criterion.");
        foreach(var item in comparisons)
        {
            if(item is not JsonObject comparison || GetString(comparison,"a").Trim().Length==0 || comparison["b"] is not JsonValue
                || !new[]{"MASK","NOTMASK","NEQ","NNE","NGT","NGE","NLT","NLE","DGT","DLT"}.Contains(GetString(comparison,"test")))
                throw new InvalidOperationException("Select a valid conditional comparison and value.");
        }
    }
    public static void Verify(JsonObject proposed, JsonObject persisted, JsonObject original)
    {
        // The PUT carries the fresh complete definition. Verify ordered topology and changes,
        // rather than requiring unchanged server-owned properties to keep their wire form.
        var before = Flatten(original["steps"]).ToDictionary(w => GetString(w[Kind(w)]!.AsObject(), "ID"), w => w[Kind(w)]!.AsObject());
        Compare(proposed["steps"], persisted["steps"], null, "steps");

        void Compare(JsonNode? expected, JsonNode? actual, JsonNode? previous, string path, bool verifyAll = false)
        {
            if (expected is JsonObject obj)
            {
                if (actual is not JsonObject target) throw new AdvancedTaskReadbackException(path);
                if (GetString(obj, "ID") is { Length: > 0 } id && before.TryGetValue(id, out var old)) previous = old;
                if (obj.ContainsKey("When") && (obj["Otherwise"] is JsonObject) != (GetNode(target, "Otherwise") is JsonObject))
                    throw new AdvancedTaskReadbackException(path + ".Otherwise");
                var baseline = previous as JsonObject;
                foreach (var pair in obj)
                {
                    var field = pair.Key;
                    if (field.StartsWith("__", StringComparison.Ordinal) || field == "InvalidOverride" || pair.Value is null || IsSecret(field)
                        || field.Equals("value", StringComparison.OrdinalIgnoreCase) && GetBool(obj, "secure")) continue;
                    var value = GetNode(target, field);
                    var prior = GetNode(baseline, field);
                    var structural = field is "ID" or "Disabled" or "steps" or "When" or "Otherwise" or "Criteria";
                    if (!verifyAll && !structural && pair.Value is JsonValue && baseline is not null && JsonNode.DeepEquals(pair.Value, prior)) continue;
                    if (field == "Disabled" || field.StartsWith("UseDef", StringComparison.Ordinal))
                    {
                        var defaultValue = field != "Disabled";
                        if (GetBool(obj, field, defaultValue) != GetBool(target, field, defaultValue)) throw new AdvancedTaskReadbackException(path + "." + field);
                        continue;
                    }
                    if (field == "Parameters")
                    {
                        try
                        {
                            if (ParseParameterObjects(pair.Value).Count != ParseParameterObjects(value).Count) throw new InvalidOperationException();
                            VerifyParameters(pair.Value, value);
                        }
                        catch (InvalidOperationException) { throw new AdvancedTaskReadbackException(path + ".Parameters"); }
                        continue;
                    }
                    if (field == "Path" && value is null) value = GetNode(target, "FolderName");
                    Compare(pair.Value, value, prior, path + "." + field, verifyAll || field == "Criteria");
                }
                return;
            }
            if (expected is JsonArray array)
            {
                if (array.Count == 0 && actual is null && path.EndsWith("steps", StringComparison.Ordinal)) return;
                if (actual is not JsonArray other || array.Count != other.Count) throw new AdvancedTaskReadbackException(path);
                for (var i = 0; i < array.Count; i++) Compare(array[i], other[i], (previous as JsonArray)?.ElementAtOrDefault(i), path + $"[{i}]", verifyAll);
                return;
            }
            var a = Normalize(expected?.ToString() ?? "");
            var b = Normalize(actual?.ToString() ?? "");
            if (a == b) return;
            // Both transports use numeric and Boolean flags; conversion must work in both directions.
            var booleanScalar = expected is JsonValue ev && ev.TryGetValue<bool>(out _)
                || actual is JsonValue av && av.TryGetValue<bool>(out _);
            if (booleanScalar && (a is "0" or "1" && b.Equals(a == "1" ? "true" : "false", StringComparison.OrdinalIgnoreCase)
                || b is "0" or "1" && a.Equals(b == "1" ? "true" : "false", StringComparison.OrdinalIgnoreCase))) return;
            throw new AdvancedTaskReadbackException(path);
        }
    }
    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    // Shared Task credential classification also used by the JSON value editor.
    internal static bool IsSecret(string key)=>key.Contains("Password",StringComparison.OrdinalIgnoreCase)||key.Contains("Secret",StringComparison.OrdinalIgnoreCase)||key.Equals("Account",StringComparison.OrdinalIgnoreCase);
}

// Field locations are safe to display; never include stored/submitted values in a mismatch.
public sealed class AdvancedTaskReadbackException(string path)
    : InvalidOperationException($"The saved Advanced Task did not match the submitted change at {path}.") { }
