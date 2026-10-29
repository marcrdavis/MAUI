// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IMoveItComClient
{
    Task<MoveItCreatedTask> CreateTaskAsync(
        string name,
        string taskType,
        CancellationToken cancellationToken = default);

    // Creates a complete Task from an exported MAUI configuration using a new MOVEit-generated Task ID.
    Task<MoveItTaskConfiguration> CreateTaskFromConfigurationAsync(
        string name,
        string taskXml,
        bool leaveDisabled,
        CancellationToken cancellationToken = default);

    Task<MoveItCreatedHost> CreateConfiguredHostAsync(HostBasicDefinition definition, CancellationToken cancellationToken = default);
    Task<MoveItCreatedHost> CreateHostFromConfigurationAsync(string name, string xml, CancellationToken cancellationToken = default);
    // Updates supported connection attributes in the existing checked-out Host definition.
    Task<MoveItHostConfiguration> UpdateConfiguredHostAsync(string hostId, HostBasicUpdate update, CancellationToken cancellationToken = default);

    // Creates a minimal persistent Host configuration that can be completed in the native Host editor.
    Task<MoveItCreatedHost> CreateHostAsync(
        string name,
        string hostType,
        CancellationToken cancellationToken = default);

    // Creates a minimal persistent custom Script configuration that is completed in the native Script editor.
    Task<MoveItCreatedScript> CreateScriptAsync(
        string name,
        string language,
        CancellationToken cancellationToken = default);

    // Purpose: Creates a Date List through the MOVEit COM configuration API.
    Task<MoveItCreatedDateList> CreateDateListAsync(string name, string description, CancellationToken cancellationToken = default);
    // Purpose: Updates date list basic details while preserving the applicable guardrails.
    Task UpdateDateListBasicDetailsAsync(string dateListId, string name, string description, CancellationToken cancellationToken = default);
    // Purpose: Returns date list configuration.
    Task<MoveItDateListConfiguration> GetDateListConfigurationAsync(string dateListId, CancellationToken cancellationToken = default);
    // Purpose: Restores date list configuration from a previously captured configuration.
    Task<MoveItDateListConfiguration> RestoreDateListConfigurationAsync(string dateListId, string dateListXml, CancellationToken cancellationToken = default);
    // Purpose: Deletes date list after applying the required guardrails.
    Task DeleteDateListAsync(string dateListId, CancellationToken cancellationToken = default);
    // Purpose: Creates a Global Parameter through the MOVEit COM configuration API.
    Task CreateGlobalParameterAsync(string name, string value, bool secure, CancellationToken cancellationToken = default);
    Task RecoverGlobalParameterCheckoutAsync(CancellationToken cancellationToken = default);
    Task<GlobalParameterEditSnapshot> GetGlobalParameterEditSnapshotAsync(string name, CancellationToken cancellationToken = default);
    // Purpose: Updates global parameter while preserving the applicable guardrails.
    Task UpdateGlobalParameterAsync(string originalName, string name, string value, bool secure, bool preserveExistingSecureValue, CancellationToken cancellationToken = default);
    // Purpose: Deletes global parameters after applying the required guardrails.
    Task DeleteGlobalParametersAsync(IEnumerable<string> names, CancellationToken cancellationToken = default);
    // Purpose: Deletes SSH client key after applying the required guardrails.
    Task DeleteSshClientKeyAsync(string keyId, CancellationToken cancellationToken = default);
    // Purpose: Updates script basic details while preserving the applicable guardrails.
    Task UpdateScriptBasicDetailsAsync(string scriptId, string name, string description, CancellationToken cancellationToken = default);

    Task UpdateTaskBasicDetailsAsync(
        string taskId,
        string name,
        string description,
        string operatorNotes,
        CancellationToken cancellationToken = default);

    // Purpose: Set task enabled.
    Task SetTaskEnabledAsync(string taskId, bool enabled, CancellationToken cancellationToken = default);

    // Reads the complete task configuration XML without checking out the task.
    Task<MoveItTaskConfiguration> GetTaskConfigurationAsync(string taskId, CancellationToken cancellationToken = default);

    // Restores a complete task configuration XML document through CheckoutItem/ModifyItem.
    Task<MoveItTaskConfiguration> RestoreTaskConfigurationAsync(string taskId, string taskXml, CancellationToken cancellationToken = default);

    // Applies a transformation under the native Task checkout lock, preserving unrelated XML fields.
    Task<MoveItTaskConfiguration> ModifyTaskConfigurationAsync(string taskId, Func<string, string> transform, CancellationToken cancellationToken = default);

    // Updates the common Host Friendly Name and Description through CheckoutItem/ModifyItem.
    Task UpdateHostBasicDetailsAsync(string hostId, string name, string description, CancellationToken cancellationToken = default);

    // Reads the complete Host configuration XML without checking out the Host.
    Task<MoveItHostConfiguration> GetHostConfigurationAsync(string hostId, CancellationToken cancellationToken = default);

    // Restores a complete Host configuration XML document through CheckoutItem/ModifyItem.
    Task<MoveItHostConfiguration> RestoreHostConfigurationAsync(string hostId, string hostXml, CancellationToken cancellationToken = default);

    // Permanently deletes one Host through the MOVEit Windows API.
    Task DeleteHostAsync(string hostId, CancellationToken cancellationToken = default);

    // Reads the complete custom Script XML without checking it out.
    Task<MoveItScriptConfiguration> GetScriptConfigurationAsync(string scriptId, CancellationToken cancellationToken = default);

    // Restores a complete custom Script configuration through CheckoutItem/ModifyItem.
    Task<MoveItScriptConfiguration> RestoreScriptConfigurationAsync(string scriptId, string scriptXml, CancellationToken cancellationToken = default);

    // Permanently deletes one custom Script through the MOVEit Windows API.
    Task DeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default);
}

public sealed record MoveItCreatedTask(string Id, string Name, string TaskType);
public sealed record MoveItCreatedHost(string Id, string Name, string HostType, string HostTypeCode);
public sealed record MoveItCreatedScript(string Id, string Name, string LanguageCode);
public sealed record MoveItCreatedDateList(string Id, string Name, string Description);
public sealed record MoveItTaskConfiguration(string Id, string Name, string Xml);
public sealed record MoveItHostConfiguration(string Id, string Name, string TypeCode, string Xml);
public sealed record MoveItScriptConfiguration(string Id, string Name, string LanguageCode, string Xml);
public sealed record MoveItDateListConfiguration(string Id, string Name, string Xml);

public sealed class MoveItComException : Exception
{
    public int? ErrorCode { get; }

    // Initializes a MOVEit COM exception with the optional API error code and inner exception.
    public MoveItComException(string message, int? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Isolates the minimal MOVEit Automation Windows COM API wrapper here while REST remains the normal read/reporting path.
/// COM is used only for configuration changes that REST does not implement consistently across task types.
/// </summary>
public sealed class MoveItComClient : IMoveItComClient
{
    private readonly IMoveItSessionService _sessionService;
    private readonly ILogger<MoveItComClient> _logger;

    // Initializes the MOVEit COM client with session access and logging.
    public MoveItComClient(IMoveItSessionService sessionService, ILogger<MoveItComClient> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    // Creates the smallest persistent task configuration needed for the selected task type, then read it
    // back from MOVEit before reporting success. Complex task configuration remains in the native Web Admin editor.
    // Purpose: Create a minimal persistent MOVEit task through the COM API.
    public Task<MoveItCreatedTask> CreateTaskAsync(
        string name,
        string taskType,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Task Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("Task Name cannot exceed 254 characters.");

        var normalizedTaskType = NormalizeTaskType(taskType);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateTaskCore(normalizedName, normalizedTaskType), cancellationToken);
    }


    // Creates a complete Task from a validated exported configuration. Only the root Task identity/state
    // is rewritten; embedded step/resource identifiers are preserved exactly as exported.
    public Task<MoveItTaskConfiguration> CreateTaskFromConfigurationAsync(
        string name,
        string taskXml,
        bool leaveDisabled,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Task Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("Task Name cannot exceed 254 characters.");
        if (string.IsNullOrWhiteSpace(taskXml)) throw new InvalidOperationException("Task configuration XML is required.");

        var template = ParsePortableTaskConfiguration(taskXml, "import file");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateTaskFromConfigurationCore(normalizedName, template, leaveDisabled), cancellationToken);
    }

    // Purpose: Creates task from configuration core.
    private MoveItTaskConfiguration CreateTaskFromConfigurationCore(string name, MoveItTaskConfiguration template, bool leaveDisabled)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit task import requires Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before importing a task.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null) throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server.");
            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost); api.SetUser(state.Username); api.SetPassword(state.Password);
            if (!ComResultSucceeded(api.Connect())) throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            string taskId = (Convert.ToString(api.SuggestItemID("Task"), CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            if (taskId.Length == 0) throw BuildApiException(comObject!, "MOVEit could not generate an ID for the imported task");

            var root = XElement.Parse(template.Xml, LoadOptions.PreserveWhitespace);
            root.SetAttributeValue("ID", taskId);
            root.SetAttributeValue("Name", name);
            if (leaveDisabled) root.SetAttributeValue("Active", "0");
            var importXml = root.ToString(SaveOptions.DisableFormatting);

            if (!ComResultSucceeded(api.CreateItem("Task", taskId, importXml, string.Empty)))
                throw BuildApiException(comObject!, $"MOVEit rejected import of task '{name}'");

            string savedXml = Convert.ToString(api.GetItemXML("Task", taskId, false), CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedXml))
                throw BuildApiException(comObject!, $"MOVEit imported task '{name}' (ID {taskId}), but it could not be read back for verification");

            var saved = ParseTaskConfiguration(savedXml, taskId, "imported task");
            if (!string.Equals(saved.Name, name, StringComparison.Ordinal))
                throw new MoveItComException($"MOVEit imported Task ID {taskId}, but read-back verification returned Name='{saved.Name}' instead of '{name}'.");

            _logger.LogInformation("Imported MOVEit task {TaskId} ({TaskName}) from a MAUI task export.", taskId, name);
            return saved;
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex) { throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex); }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        { throw new MoveItComException($"Unexpected MOVEit COM task-import error: {ex.Message}", null, ex); }
        finally
        {
            if (api is not null) { try { api.Disconnect(); } catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during task-import cleanup."); } }
            ReleaseComObject(comObject, "task import");
        }
    }

    // Purpose: Create task core.
    private MoveItCreatedTask CreateTaskCore(string name, string taskType)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new MoveItComException("MOVEit task creation requires Windows because the MOVEit Automation COM API is a Windows component.");
        }

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
        {
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before creating a task.");
        }

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
            {
                throw new MoveItComException(
                    "MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before creating tasks.");
            }

            comObject = Activator.CreateInstance(apiType)
                ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;

            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
            {
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");
            }

            object? suggestedTaskId = api.SuggestItemID("Task");
            string taskId = (Convert.ToString(suggestedTaskId, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            if (taskId.Length == 0)
            {
                throw BuildApiException(comObject!, "MOVEit could not generate an ID for the new task");
            }

            string taskXml = BuildNewTaskXml(taskId, name, taskType);
            object? createResult = api.CreateItem("Task", taskId, taskXml, string.Empty);
            if (!ComResultSucceeded(createResult))
            {
                throw BuildApiException(comObject!, $"MOVEit rejected creation of task '{name}'");
            }

            // The COM TRUE/FALSE result alone is not trusted. Read the persistent configuration back and
            // verify the identity/type that MAUI requested before handing the user to the native editor.
            object? savedXmlResult = api.GetItemXML("Task", taskId, false);
            string savedXml = Convert.ToString(savedXmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedXml))
            {
                throw BuildApiException(comObject!, $"MOVEit created task '{name}' (ID {taskId}), but it could not be read back for verification");
            }
            VerifyCreatedTask(savedXml, taskId, name, taskType);

            _logger.LogInformation(
                "Created MOVEit {TaskType} task {TaskId} ({TaskName}) through the Windows COM API.",
                taskType, taskId, name);

            return new MoveItCreatedTask(taskId, name, taskType);
        }
        catch (MoveItComException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM task-creation error: {ex.Message}", null, ex);
        }
        finally
        {
            if (api is not null)
            {
                try { api.Disconnect(); }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "MOVEit COM disconnect failed during task-creation cleanup.");
                }
            }

            ReleaseComObject(comObject, "task creation");
        }
    }

    // Creates a minimal persistent custom Script and verifies the saved identity before returning it to the native editor.
    public Task<MoveItCreatedScript> CreateScriptAsync(
        string name,
        string language,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Script Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("Script Name cannot exceed 254 characters.");

        var normalizedLanguage = NormalizeScriptLanguage(language);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateScriptCore(normalizedName, normalizedLanguage), cancellationToken);
    }

    // Performs the late-bound COM calls required to create and verify one custom Script shell.
    private MoveItCreatedScript CreateScriptCore(string name, string language)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Script creation requires Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before creating a Script.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before creating Scripts.");

            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? suggestedScriptId = api.SuggestItemID("Script");
            string scriptId = (Convert.ToString(suggestedScriptId, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            if (scriptId.Length == 0)
                throw BuildApiException(comObject!, "MOVEit could not generate an ID for the new Script");

            string scriptXml = BuildNewScriptXml(scriptId, name, language);
            object? createResult = api.CreateItem("Script", scriptId, scriptXml, string.Empty);
            if (!ComResultSucceeded(createResult))
                throw BuildApiException(comObject!, $"MOVEit rejected creation of Script '{name}'");

            object? savedXmlResult = api.GetItemXML("Script", scriptId, false);
            string savedXml = Convert.ToString(savedXmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedXml))
                throw BuildApiException(comObject!, $"MOVEit created Script '{name}' (ID {scriptId}), but it could not be read back for verification");

            var saved = ParseScriptConfiguration(savedXml, scriptId, "new Script");
            if (!string.Equals(saved.Name, name, StringComparison.Ordinal))
                throw new MoveItComException($"MOVEit created Script ID {scriptId}, but read-back verification returned Name='{saved.Name}' instead of '{name}'.");

            _logger.LogInformation("Created MOVEit custom Script {ScriptId} ({ScriptName}) language {Language} through the Windows COM API.", scriptId, name, language);
            return new MoveItCreatedScript(scriptId, name, language);
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM Script-creation error: {ex.Message}", null, ex);
        }
        finally
        {
            if (api is not null)
            {
                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Script-creation cleanup."); }
            }
            ReleaseComObject(comObject, "Script creation");
        }
    }

    // Creates a minimal persistent Date List. Empty Date Lists are valid MOVEit resources.
    public Task<MoveItCreatedDateList> CreateDateListAsync(string name, string description, CancellationToken cancellationToken = default)
    {
        var n=(name??string.Empty).Trim(); if(n.Length==0) throw new InvalidOperationException("Date List Name is required.");
        cancellationToken.ThrowIfCancellationRequested(); return Task.Run(()=>CreateDateListCore(n,description??string.Empty),cancellationToken);
    }

    // Purpose: Creates date list core.
    private MoveItCreatedDateList CreateDateListCore(string name,string description)
    {
        if(!OperatingSystem.IsWindows()) throw new MoveItComException("MOVEit Date List creation requires Windows because the MOVEit Automation COM API is a Windows component.");
        var state=_sessionService.Get()??throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if(string.IsNullOrWhiteSpace(state.Password)) throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before creating a Date List.");
        var host=ResolveAutomationHost(state); object? obj=null; dynamic? api=null;
        try{var t=Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");if(t is null)throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server.");obj=Activator.CreateInstance(t)??throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");api=obj;api.SetHost(host);api.SetUser(state.Username);api.SetPassword(state.Password);if(!ComResultSucceeded(api.Connect()))throw BuildApiException(obj,$"COM login to MOVEit Automation host '{host}' failed");string id=(Convert.ToString(api.SuggestItemID("DateList"),CultureInfo.InvariantCulture)??"").Trim();if(id.Length==0)throw BuildApiException(obj,"MOVEit could not generate an ID for the new Date List");var xml=BuildNewDateListXml(id,name,description);if(!ComResultSucceeded(api.CreateItem("DateList",id,xml,string.Empty)))throw BuildApiException(obj,$"MOVEit rejected creation of Date List '{name}'");var saved=Convert.ToString(api.GetItemXML("DateList",id,false),CultureInfo.InvariantCulture)??"";var parsed=ParseDateListConfiguration(saved,id,"new Date List");return new MoveItCreatedDateList(id,parsed.Name,description);}
        catch(MoveItComException){throw;}catch(COMException ex){throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}",null,ex);}catch(Exception ex) when(ex is not MoveItAuthenticationException){throw new MoveItComException($"Unexpected MOVEit COM Date List creation error: {ex.Message}",null,ex);}
        finally
        {
            if (api is not null)
            {
                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Date List creation cleanup."); }
            }
            ReleaseComObject(obj, "Date List creation");
        }
    }

    // Purpose: Updates date list basic details while preserving the applicable guardrails.
    public Task UpdateDateListBasicDetailsAsync(string dateListId,string name,string description,CancellationToken cancellationToken=default)
        => UpdateNamedConfigurationAsync("DateList",dateListId,name,description,"Description",cancellationToken);

    // Purpose: Updates script basic details while preserving the applicable guardrails.
    public Task UpdateScriptBasicDetailsAsync(string scriptId,string name,string description,CancellationToken cancellationToken=default)
        => UpdateNamedConfigurationAsync("Script",scriptId,name,description,"Description",cancellationToken);

    // Purpose: Updates named configuration.
    private Task UpdateNamedConfigurationAsync(string itemType,string itemId,string name,string description,string descriptionElement,CancellationToken cancellationToken)
    {
        if(string.IsNullOrWhiteSpace(itemId))throw new ArgumentException($"{itemType} ID is required.");if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException($"{itemType} Name is required.");
        var id=itemId.Trim();return Task.Run(()=>{if(!OperatingSystem.IsWindows())throw new MoveItComException($"MOVEit {itemType} editing requires Windows.");var state=_sessionService.Get()??throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");var host=ResolveAutomationHost(state);object? obj=null;dynamic? api=null;bool co=false;try{var t=Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");if(t is null)throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server.");obj=Activator.CreateInstance(t)!;api=obj;api.SetHost(host);api.SetUser(state.Username);api.SetPassword(state.Password);if(!ComResultSucceeded(api.Connect()))throw BuildApiException(obj,$"COM login to MOVEit Automation host '{host}' failed");string xml=Convert.ToString(api.CheckoutItem(itemType,id),CultureInfo.InvariantCulture)??"";if(xml.Length==0)throw BuildApiException(obj,$"Unable to check out {itemType} '{id}'");co=true;var root=XElement.Parse(xml,LoadOptions.PreserveWhitespace);ObjectEditVersion.Verify(itemType,id,ObjectEditVersion.Named((string?)root.Attribute("Name")??"",ObjectEditVersion.InformationValue(root, descriptionElement)));root.SetAttributeValue("Name",name.Trim());var d=root.Elements().FirstOrDefault(e=>e.Name.LocalName.Equals(descriptionElement,StringComparison.OrdinalIgnoreCase));if(d is null)root.Add(new XElement(descriptionElement,description??""));else d.Value=description??"";if(!ComResultSucceeded(api.ModifyItem(itemType,id,root.ToString(SaveOptions.DisableFormatting),false)))throw BuildApiException(obj,$"MOVEit rejected changes to {itemType} '{id}'");}
        finally
        {
            Exception? releaseFailure = null;
            if (api is not null)
            {
                if (co) releaseFailure = ReleaseObjectCheckout(api, itemType, id);

                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during {ItemType} edit cleanup for {ItemId}.", itemType, id); }
            }
            ReleaseComObject(obj, $"{itemType} edit {id}");
            if (releaseFailure is not null) throw new ObjectCheckoutReleaseException(itemType, id, releaseFailure);
        }}, cancellationToken);
    }

    // Purpose: Returns date list configuration.
    public Task<MoveItDateListConfiguration> GetDateListConfigurationAsync(string dateListId,CancellationToken cancellationToken=default)
        => GetDateListConfigurationCoreAsync(dateListId,false,null,cancellationToken);
    // Purpose: Restores date list configuration from a previously captured configuration.
    public Task<MoveItDateListConfiguration> RestoreDateListConfigurationAsync(string dateListId,string dateListXml,CancellationToken cancellationToken=default)
        => GetDateListConfigurationCoreAsync(dateListId,true,dateListXml,cancellationToken);

    // Purpose: Returns date list configuration core.
    private Task<MoveItDateListConfiguration> GetDateListConfigurationCoreAsync(
        string dateListId,
        bool restore,
        string? snapshot,
        CancellationToken cancellationToken)
    {
        var id = (dateListId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Date List ID is required.");
        if (restore && string.IsNullOrWhiteSpace(snapshot)) throw new ArgumentException("Date List XML is required.");
        if (restore) _ = ParseDateListConfiguration(snapshot!, id, "undo snapshot");

        return Task.Run(
            () => GetDateListConfigurationCore(id, restore, snapshot),
            cancellationToken);
    }

    // Purpose: Returns date list configuration core.
    private MoveItDateListConfiguration GetDateListConfigurationCore(
        string dateListId,
        bool restore,
        string? snapshot)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Date List configuration operations require Windows.");

        var state = _sessionService.Get()
            ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        var checkedOut = false;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server.");

            comObject = Activator.CreateInstance(apiType)
                ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject, $"COM login to MOVEit Automation host '{automationHost}' failed");

            if (restore)
            {
                var currentXml = Convert.ToString(
                    api.CheckoutItem("DateList", dateListId),
                    CultureInfo.InvariantCulture) ?? string.Empty;
                if (currentXml.Length == 0)
                    throw BuildApiException(comObject, $"Unable to check out Date List '{dateListId}'");

                checkedOut = true;
                if (ObjectEditVersion.IsRequired("EnvironmentCopy"))
                    ObjectEditVersion.Verify("EnvironmentCopy", "DateList|" + dateListId, MoveItAutomationUI.Features.EnvironmentCopy.EnvironmentCopyRules.RevisionXml(currentXml));
                object? modifyResult = api.ModifyItem("DateList", dateListId, snapshot!, false);
                if (!ComResultSucceeded(modifyResult))
                    throw BuildApiException(comObject, $"MOVEit rejected the configuration restore for Date List '{dateListId}'");
            }

            var xml = Convert.ToString(
                api.GetItemXML("DateList", dateListId, false),
                CultureInfo.InvariantCulture) ?? string.Empty;

            return ParseDateListConfiguration(
                xml,
                dateListId,
                restore ? "restored Date List" : "Date List snapshot");
        }
        finally
        {
            Exception? releaseFailure = null;
            if (api is not null)
            {
                if (checkedOut) releaseFailure = ReleaseObjectCheckout(api, "DateList", dateListId);

                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Date List cleanup for {DateListId}.", dateListId); }
            }

            ReleaseComObject(comObject, $"Date List {(restore ? "undo" : "read")} {dateListId}");
            if (releaseFailure is not null) throw new ObjectCheckoutReleaseException("DateList", dateListId, releaseFailure);
        }
    }

    // Purpose: Deletes SSH client key after applying the required guardrails.
    public Task DeleteSshClientKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var id = (keyId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("SSH Client Key ID is required.", nameof(keyId));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new MoveItComException("MOVEit SSH Client Key deletion requires Windows.");
            var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
            var host = ResolveAutomationHost(state);
            object? obj = null;
            dynamic? api = null;
            try
            {
                var type = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
                if (type is null) throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server.");
                obj = Activator.CreateInstance(type) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
                api = obj;
                api.SetHost(host); api.SetUser(state.Username); api.SetPassword(state.Password);
                object? connectResult = api.Connect();
                if (!ComResultSucceeded(connectResult)) throw BuildApiException(obj, $"COM login to MOVEit Automation host '{host}' failed");
                object? deleteResult = api.DeleteItem("SSHClientKey", id);
                if (!ComResultSucceeded(deleteResult)) throw BuildApiException(obj, $"MOVEit rejected deletion of SSH Client Key '{id}'");
            }
            finally
            {
                if (api is not null)
                {
                    try { api.Disconnect(); }
                    catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during SSH Client Key deletion cleanup for {KeyId}.", id); }
                }
                ReleaseComObject(obj, $"SSH Client Key delete {id}");
            }
        }, cancellationToken);
    }

    // Purpose: Deletes date list after applying the required guardrails.
    public Task DeleteDateListAsync(string dateListId,CancellationToken cancellationToken=default)
    {
        var id = (dateListId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Date List ID is required.");

        return Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new MoveItComException("MOVEit Date List deletion requires Windows.");
            var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
            var host = ResolveAutomationHost(state);
            object? obj = null;
            dynamic? api = null;
            try
            {
                var t = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
                if (t is null) throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server.");
                obj = Activator.CreateInstance(t)!;
                api = obj;
                api.SetHost(host);
                api.SetUser(state.Username);
                api.SetPassword(state.Password);
                if (!ComResultSucceeded(api.Connect())) throw BuildApiException(obj, $"COM login to MOVEit Automation host '{host}' failed");
                if (!ComResultSucceeded(api.DeleteItem("DateList", id))) throw BuildApiException(obj, $"MOVEit rejected deletion of Date List '{id}'");
            }
            finally
            {
                if (api is not null)
                {
                    try { api.Disconnect(); }
                    catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Date List deletion cleanup for {DateListId}.", id); }
                }
                ReleaseComObject(obj, $"Date List delete {id}");
            }
        }, cancellationToken);
    }


    // Global Parameters are a collection section in MOVEit. Every write checks out the current collection,
    // changes only the requested Parameter node(s), submits the entire checked-out section, and releases it.
    public Task CreateGlobalParameterAsync(string name, string value, bool secure, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeGlobalParameterName(name);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => ModifyGlobalParametersCore(root =>
        {
            if (FindGlobalParameterElement(root, normalizedName) is not null)
                throw new InvalidOperationException($"A Global Parameter named '{normalizedName}' already exists.");

            var parameter = new XElement(root.Name.Namespace + "Parameter");
            SetGlobalParameterShape(parameter, normalizedName, value ?? string.Empty, secure);
            root.Add(parameter);
        }, $"create Global Parameter '{normalizedName}'"), cancellationToken);
    }

    // Purpose: Updates global parameter while preserving the applicable guardrails.
    // Read the exact native parameter used by the edit form, including an opaque revision for suppressed secrets.
    public Task<GlobalParameterEditSnapshot> GetGlobalParameterEditSnapshotAsync(string name, CancellationToken cancellationToken = default)
    {
        var id = NormalizeGlobalParameterName(name);
        return Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new MoveItComException("MOVEit Global Parameter editing requires Windows.");
            var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
            object? comObject = null;
            dynamic? api = null;
            try
            {
                var type = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI")
                    ?? throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server.");
                comObject = Activator.CreateInstance(type)!;
                api = comObject;
                api.SetHost(ResolveAutomationHost(state)); api.SetUser(state.Username); api.SetPassword(state.Password);
                if (!ComResultSucceeded(api.Connect())) throw BuildApiException(comObject, "MOVEit COM login failed");
                string xml = Convert.ToString(api.GetItemXML("GlobalParameters", string.Empty, false), CultureInfo.InvariantCulture) ?? "";
                if (xml.Length == 0) throw BuildApiException(comObject, "Unable to read Global Parameters");
                var parameter = FindGlobalParameterElement(XElement.Parse(xml), id)
                    ?? throw new InvalidOperationException($"Global Parameter '{id}' no longer exists.");
                var secure = ((string?)parameter.Attribute("Secure") ?? "0") == "1";
                return new GlobalParameterEditSnapshot(id, secure ? "" : ((string?)parameter.Attribute("value") ?? parameter.Value), secure,
                    ObjectEditVersion.Create("GlobalParameter", id, ObjectEditVersion.Xml(parameter)));
            }
            finally
            {
                if (api is not null) { try { api.Disconnect(); } catch (Exception ex) { _logger.LogDebug(ex, "Global Parameter read disconnect failed."); } }
                ReleaseComObject(comObject, "Global Parameter editor read");
            }
        }, cancellationToken);
    }

    public Task UpdateGlobalParameterAsync(
        string originalName,
        string name,
        string value,
        bool secure,
        bool preserveExistingSecureValue,
        CancellationToken cancellationToken = default)
    {
        var original = NormalizeGlobalParameterName(originalName);
        var updatedName = NormalizeGlobalParameterName(name);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => ModifyGlobalParametersCore(root =>
        {
            var parameter = FindGlobalParameterElement(root, original)
                ?? throw new InvalidOperationException($"Global Parameter '{original}' no longer exists.");
            ObjectEditVersion.Verify("GlobalParameter", original, ObjectEditVersion.Xml(parameter));
            var duplicate = FindGlobalParameterElement(root, updatedName);
            if (duplicate is not null && !ReferenceEquals(duplicate, parameter))
                throw new InvalidOperationException($"A Global Parameter named '{updatedName}' already exists.");

            if (preserveExistingSecureValue)
            {
                SetGlobalParameterName(parameter, original, updatedName);
                return;
            }

            if (!parameter.Name.LocalName.Equals("Parameter", StringComparison.OrdinalIgnoreCase))
                throw new MoveItComException($"MOVEit returned an unexpected Global Parameter XML node '{parameter.Name.LocalName}'. No changes were submitted.");

            SetGlobalParameterShape(parameter, updatedName, value ?? string.Empty, secure);
        }, $"update Global Parameter '{original}'"), cancellationToken);
    }

    // Purpose: Deletes global parameters after applying the required guardrails.
    public Task DeleteGlobalParametersAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        var normalized = (names ?? Array.Empty<string>())
            .Select(NormalizeGlobalParameterName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0) throw new ArgumentException("Select at least one Global Parameter.", nameof(names));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => ModifyGlobalParametersCore(root =>
        {
            foreach (var name in normalized)
            {
                var parameter = FindGlobalParameterElement(root, name)
                    ?? throw new InvalidOperationException($"Global Parameter '{name}' no longer exists.");
                parameter.Remove();
            }
        }, normalized.Count == 1 ? $"delete Global Parameter '{normalized[0]}'" : $"delete {normalized.Count} Global Parameters"), cancellationToken);
    }

    // Explicit recovery uses CheckoutItem's documented bForce parameter, then ReleaseItem.
    // https://docs.ipswitch.com/MOVEit/Automation2024/API/Windows/APIReference.htm
    public Task RecoverGlobalParameterCheckoutAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => ModifyGlobalParametersCore(_ => { }, "recover the Global Parameters checkout", recoverOnly: true), cancellationToken);

    // Purpose: Applies a Global Parameter mutation to a checked-out collection and submits it.
    private void ModifyGlobalParametersCore(Action<XElement> mutator, string operation, bool recoverOnly = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Global Parameter changes require Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (recoverOnly && state.Info?.IsAdmin != true)
            throw new InvalidOperationException("Only a MOVEit administrator can override checkout.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before changing Global Parameters.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        var checkedOut = false;
        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server.");
            comObject = Activator.CreateInstance(apiType)
                ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);
            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject, $"COM login to MOVEit Automation host '{automationHost}' failed");

            var xml = Convert.ToString(api.CheckoutItem("GlobalParameters", string.Empty, recoverOnly), CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(xml))
            {
                var error = BuildApiException(comObject, "Unable to check out the Global Parameters collection");
                if (ObjectCheckoutRejectedException.DescribesLock(error.Message)) throw new ObjectCheckoutRejectedException(error.Message);
                throw error;
            }
            checkedOut = true;

            XElement root;
            try { root = XElement.Parse(xml, LoadOptions.PreserveWhitespace); }
            catch (Exception ex) { throw new MoveItComException("MOVEit returned invalid Global Parameters XML.", null, ex); }
            if (!root.Name.LocalName.Equals("GlobalParameters", StringComparison.OrdinalIgnoreCase))
                throw new MoveItComException($"MOVEit returned unexpected XML root '{root.Name.LocalName}' for GlobalParameters.");

            if (recoverOnly) return; // finally releases this confirmed checkout without modifying configuration.

            mutator(root);
            var updatedXml = root.ToString(SaveOptions.DisableFormatting);
            object? modifyResult = api.ModifyItem("GlobalParameters", string.Empty, updatedXml, false);
            if (!ComResultSucceeded(modifyResult))
                throw BuildApiException(comObject, $"MOVEit rejected the request to {operation}");

            _logger.LogInformation("Successfully used MOVEit COM to {Operation}.", operation);
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex) { throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex); }
        catch (Exception ex) when (ex is not MoveItApiException && ex is not InvalidOperationException && ex is not ArgumentException)
        { throw new MoveItComException($"Unexpected MOVEit COM error while attempting to {operation}: {ex.Message}", null, ex); }
        finally
        {
            Exception? releaseFailure = null;
            if (api is not null)
            {
                if (checkedOut) releaseFailure = ReleaseObjectCheckout(api, "GlobalParameters", string.Empty);

                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed after Global Parameters operation."); }
            }
            ReleaseComObject(comObject, "Global Parameters operation");
            if (releaseFailure is not null) throw new ObjectCheckoutReleaseException("GlobalParameters", string.Empty, releaseFailure);
        }
    }

    // Purpose: Finds global parameter element.
    private static XElement? FindGlobalParameterElement(XElement root, string name)
    {
        foreach (var element in root.Elements())
        {
            if (element.Name.LocalName.Equals("Parameter", StringComparison.OrdinalIgnoreCase))
            {
                var parameterName = ((string?)element.Attribute("Name") ?? string.Empty).Trim();
                if (parameterName.Equals(name, StringComparison.OrdinalIgnoreCase)) return element;
            }
            else if (element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }
        }
        return null;
    }

    // Purpose: Sets global parameter name.
    private static void SetGlobalParameterName(XElement parameter, string originalName, string newName)
    {
        if (parameter.Name.LocalName.Equals("Parameter", StringComparison.OrdinalIgnoreCase))
        {
            parameter.SetAttributeValue("Name", newName);
            return;
        }

        if (!parameter.Name.LocalName.Equals(originalName, StringComparison.OrdinalIgnoreCase))
            throw new MoveItComException($"MOVEit returned an unexpected Global Parameter XML node '{parameter.Name.LocalName}'.");
        try { parameter.Name = XName.Get(newName, parameter.Name.NamespaceName); }
        catch (Exception ex) { throw new InvalidOperationException($"Global Parameter Name '{newName}' is not valid for the XML format returned by MOVEit.", ex); }
    }

    // Purpose: Sets global parameter shape.
    private static void SetGlobalParameterShape(XElement parameter, string name, string value, bool secure)
    {
        if (!parameter.Name.LocalName.Equals("Parameter", StringComparison.OrdinalIgnoreCase))
            throw new MoveItComException($"MOVEit returned an unexpected Global Parameter XML node '{parameter.Name.LocalName}'.");

        // MOVEit 2025.x represents each Global Parameter as a Parameter item whose collection
        // properties include value, Name, Secure, and (for secure values) DisplayValue.
        // Preserve unrelated attributes, but normalize the value/security properties we own.
        parameter.RemoveNodes();
        foreach (var attribute in parameter.Attributes()
                     .Where(attribute => attribute.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase)
                                      || attribute.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase)
                                      || attribute.Name.LocalName.Equals("Secure", StringComparison.OrdinalIgnoreCase)
                                      || attribute.Name.LocalName.Equals("DisplayValue", StringComparison.OrdinalIgnoreCase))
                     .ToList())
            attribute.Remove();

        parameter.SetAttributeValue("value", value);
        parameter.SetAttributeValue("Name", name);
        parameter.SetAttributeValue("Secure", secure ? "1" : "0");
        if (secure)
            parameter.SetAttributeValue("DisplayValue", "(suppressed)");
    }

    // Purpose: Normalizes global parameter name.
    private static string NormalizeGlobalParameterName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) throw new InvalidOperationException("Global Parameter Name is required.");
        if (value.Length > 254) throw new InvalidOperationException("Global Parameter Name cannot exceed 254 characters.");
        return value;
    }

    // Creates a minimal persistent Host and verifies the saved Host identity/type before returning it to the native editor.
    public Task<MoveItCreatedHost> CreateHostAsync(
        string name,
        string hostType,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Host Name is required.");

        var normalizedHostType = NormalizeHostType(hostType);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateHostCore(normalizedName, normalizedHostType), cancellationToken);
    }

    public Task<MoveItCreatedHost> CreateConfiguredHostAsync(HostBasicDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateHostCore(definition.Name, definition.HostType, definition), cancellationToken);
    }

    public Task<MoveItCreatedHost> CreateHostFromConfigurationAsync(string name, string xml, CancellationToken cancellationToken = default)
    {
        var cloneName = (name ?? "").Trim();
        if (cloneName.Length == 0 || cloneName.Length > 254) throw new ArgumentException("Enter a Host Name from 1 to 254 characters.");
        var source = HostCloneDefinition.Read(xml);
        var type = NormalizeHostType(source.Name.LocalName);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateHostCore(cloneName, type, nativeTemplate: source), cancellationToken);
    }

    // Performs the late-bound COM calls required to create and verify one Host.
    private MoveItCreatedHost CreateHostCore(string name, string hostType, HostBasicDefinition? definition = null, XElement? nativeTemplate = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Host creation requires Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before creating a Host.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        bool createSucceeded = false, createAttempted = false, createRejected = false;
        string createdId = string.Empty;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before creating Hosts.");

            comObject = Activator.CreateInstance(apiType)
                ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;

            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? suggestedHostId = api.SuggestItemID("Host");
            string hostId = (Convert.ToString(suggestedHostId, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            if (hostId.Length == 0)
                throw BuildApiException(comObject!, "MOVEit could not generate an ID for the new Host");

            createdId = hostId;
            string hostXml = nativeTemplate is null ? BuildNewHostXml(hostId, name, hostType) : HostCloneDefinition.Compose(nativeTemplate, hostId, name);
            if (definition is not null)
            {
                // MOVEit COM expects an encrypted password in its native Host XML.
                // Never write the original plaintext into a persisted Host definition.
                string encryptedPassword = string.Empty;
                if (definition.Password.Length > 0)
                {
                    encryptedPassword = Convert.ToString(api.EncryptPW(definition.Password), CultureInfo.InvariantCulture) ?? string.Empty;
                    if (encryptedPassword.Length == 0)
                        throw BuildApiException(comObject!, "MOVEit could not encrypt the Host credential");
                }
                string sshKeyId = "0";
                if (definition.SshKeyName.Length > 0)
                {
                    string keyXml = Convert.ToString((object?)api.GetItemXML("SSHClientKey", definition.SshKeyName, true), CultureInfo.InvariantCulture) ?? string.Empty;
                    if (keyXml.Length == 0) throw new MoveItComException("The requested SSH client key was not found or is not accessible.");
                    var keyRoot = XElement.Parse(keyXml);
                    var key = keyRoot.Name.LocalName.Equals("SSHClientKey", StringComparison.OrdinalIgnoreCase)
                        ? keyRoot : keyRoot.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("SSHClientKey", StringComparison.OrdinalIgnoreCase));
                    sshKeyId = ((string?)key?.Attribute("ID") ?? string.Empty).Trim();
                    if (sshKeyId.Length == 0) throw new MoveItComException("The requested SSH client key has no usable ID.");
                }
                hostXml = HostBasicDefinitionProfile.Compose(hostXml, definition, encryptedPassword, sshKeyId);
            }
            createAttempted = true;
            object? createResult = api.CreateItem("Host", hostId, hostXml, definition?.ResourceGroup ?? string.Empty);
            if (!ComResultSucceeded(createResult))
            {
                createRejected = true;
                var failure = BuildApiException(comObject!, $"MOVEit rejected creation of Host '{name}'");
                // The documented UNC mount error 53 occurs after the configuration write.
                createSucceeded = failure.ErrorCode == 53;
                throw failure;
            }

            createSucceeded = true;
            object? savedXmlResult = api.GetItemXML("Host", hostId, false);
            string savedXml = Convert.ToString(savedXmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedXml))
                throw BuildApiException(comObject!, $"MOVEit created Host '{name}' (ID {hostId}), but it could not be read back for verification");

            VerifyCreatedHost(savedXml, hostId, name, hostType);
            if (definition is not null || nativeTemplate is not null) HostDefinitionVerification.VerifyCreate(hostXml, savedXml);
            _logger.LogInformation("Created MOVEit {HostType} Host {HostId} ({HostName}) through the Windows COM API.", hostType, hostId, name);
            return new MoveItCreatedHost(hostId, name, FriendlyHostType(hostType), hostType);
        }
        catch (Exception ex) when (createSucceeded || (nativeTemplate is not null && createAttempted && !createRejected))
        {
            throw new HostPartialSaveException("The Host creation outcome or saved configuration could not be confirmed. Review Hosts before retrying.", ex, createdId);
        }
        catch (MoveItComException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM Host-creation error: {ex.Message}", null, ex);
        }
        finally
        {
            if (api is not null)
            {
                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Host-creation cleanup."); }
            }
            ReleaseComObject(comObject, "Host creation");
        }
    }

    // Updates Friendly Name, Description, and Operator Notes using CheckoutItem/ModifyItem so Traditional,
    // Advanced, and Synchronization tasks all use the same supported configuration-edit path.
    // Purpose: Update common Task Information fields through the COM API.
    public Task UpdateTaskBasicDetailsAsync(
        string taskId,
        string name,
        string description,
        string operatorNotes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));

        string normalizedTaskId = taskId.Trim();
        string normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Task Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("Task Name cannot exceed 254 characters.");

        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => UpdateTaskBasicDetailsCore(normalizedTaskId, normalizedName, description ?? string.Empty, operatorNotes ?? string.Empty),
            cancellationToken);
    }

    // Purpose: Update task basic details core.
    private void UpdateTaskBasicDetailsCore(string taskId, string name, string description, string operatorNotes)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new MoveItComException("MOVEit task editing requires Windows because the MOVEit Automation COM API is a Windows component.");
        }

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
        {
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before editing a task.");
        }

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        var checkedOut = false;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
            {
                throw new MoveItComException(
                    "MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before enabling task edits.");
            }

            comObject = Activator.CreateInstance(apiType)
                ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;

            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
            {
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");
            }

            object? checkoutResult = api.CheckoutItem("Task", taskId);
            string taskXml = Convert.ToString(checkoutResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(taskXml))
            {
                throw BuildApiException(comObject!, $"Unable to check out task '{taskId}' for editing");
            }
            checkedOut = true;

            var taskElement = XElement.Parse(taskXml, LoadOptions.PreserveWhitespace);
            if (!taskElement.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase))
            {
                throw new MoveItComException("MOVEit returned unexpected task XML while checking out the task.");
            }

            string checkedOutTaskId = ((string?)taskElement.Attribute("ID") ?? string.Empty).Trim();
            if (!string.Equals(checkedOutTaskId, taskId, StringComparison.OrdinalIgnoreCase))
            {
                throw new MoveItComException(
                    $"MOVEit returned task ID '{checkedOutTaskId}' while MAUI was editing task ID '{taskId}'. The update was not submitted.");
            }

            ObjectEditVersion.Verify("TaskInformation", taskId, ObjectEditVersion.TaskInformation((string?)taskElement.Attribute("Name") ?? "",
                ObjectEditVersion.InformationValue(taskElement, "Description", "Desc"), ObjectEditVersion.InformationValue(taskElement, "Notes", "OperatorNotes")));
            taskElement.SetAttributeValue("Name", name);
            SetTaskInformationValue(taskElement, new[] { "Description", "Desc" }, "Description", description);
            SetTaskInformationValue(taskElement, new[] { "Notes", "OperatorNotes" }, "Notes", operatorNotes);

            string updatedXml = taskElement.ToString(SaveOptions.DisableFormatting);

            // No Groups argument is passed here. Resource-group membership is intentionally left unchanged.
            object? modifyResult = api.ModifyItem("Task", taskId, updatedXml, false);
            if (!ComResultSucceeded(modifyResult))
            {
                throw BuildApiException(comObject!, $"MOVEit rejected the update for task '{taskId}'");
            }

            _logger.LogInformation("Updated MOVEit task {TaskId} basic information through the Windows COM API.", taskId);
        }
        catch (MoveItComException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException && ex is not ObjectEditConflictException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM task-update error: {ex.Message}", null, ex);
        }
        finally
        {
            Exception? releaseFailure = null;
            if (api is not null)
            {
                if (checkedOut) releaseFailure = ReleaseObjectCheckout(api, "Task", taskId);

                try { api.Disconnect(); }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "MOVEit COM disconnect failed during task-update cleanup for task {TaskId}.", taskId);
                }
            }

            ReleaseComObject(comObject, $"task update {taskId}");
            if (releaseFailure is not null) throw new ObjectCheckoutReleaseException("Task", taskId, releaseFailure);
        }
    }


    // Changes only the top-level Active flag through the supported checkout/modify workflow.
    public Task SetTaskEnabledAsync(string taskId, bool enabled, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        string normalizedTaskId = taskId.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => SetTaskEnabledCore(normalizedTaskId, enabled), cancellationToken);
    }

    // Purpose: Set task enabled core.
    private void SetTaskEnabledCore(string taskId, bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit task enable/disable requires Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before changing task state.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        var checkedOut = false;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before changing task state.");

            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? checkoutResult = api.CheckoutItem("Task", taskId);
            string taskXml = Convert.ToString(checkoutResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(taskXml))
                throw BuildApiException(comObject!, $"Unable to check out task '{taskId}' for {(enabled ? "enable" : "disable")}");
            checkedOut = true;

            var taskElement = XElement.Parse(taskXml, LoadOptions.PreserveWhitespace);
            if (!taskElement.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase))
                throw new MoveItComException("MOVEit returned unexpected task XML while checking out the task.");

            string checkedOutTaskId = ((string?)taskElement.Attribute("ID") ?? string.Empty).Trim();
            if (!string.Equals(checkedOutTaskId, taskId, StringComparison.OrdinalIgnoreCase))
                throw new MoveItComException($"MOVEit returned task ID '{checkedOutTaskId}' while MAUI was changing task ID '{taskId}'. The update was not submitted.");

            taskElement.SetAttributeValue("Active", enabled ? "1" : "0");
            string updatedXml = taskElement.ToString(SaveOptions.DisableFormatting);
            object? modifyResult = api.ModifyItem("Task", taskId, updatedXml, false);
            if (!ComResultSucceeded(modifyResult))
                throw BuildApiException(comObject!, $"MOVEit rejected {(enabled ? "enabling" : "disabling")} task '{taskId}'");

            _logger.LogInformation("{Action} MOVEit task {TaskId} through the Windows COM API.", enabled ? "Enabled" : "Disabled", taskId);
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM task-state error: {ex.Message}", null, ex);
        }
        finally
        {
            Exception? releaseFailure = null;
            if (api is not null)
            {
                if (checkedOut) releaseFailure = ReleaseObjectCheckout(api, "Task", taskId);

                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during task-state cleanup for task {TaskId}.", taskId); }
            }

            ReleaseComObject(comObject, $"task state update {taskId}");
            if (releaseFailure is not null) throw new ObjectCheckoutReleaseException("Task", taskId, releaseFailure);
        }
    }


    // Reads a complete task XML snapshot for MAUI's one-level undo feature without obtaining a checkout lock.
    public Task<MoveItTaskConfiguration> GetTaskConfigurationAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        var normalizedTaskId = taskId.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => GetTaskConfigurationCore(normalizedTaskId), cancellationToken);
    }

    // Reads and validates the complete task XML returned by GetItemXML.
    private MoveItTaskConfiguration GetTaskConfigurationCore(string taskId)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit task snapshots require Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before opening a task editor.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before using task configuration operations.");

            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? xmlResult = api.GetItemXML("Task", taskId, false);
            string taskXml = Convert.ToString(xmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(taskXml))
                throw BuildApiException(comObject!, $"Unable to read task '{taskId}' for an undo snapshot");

            return ParseTaskConfiguration(taskXml, taskId, "snapshot");
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM task-snapshot error: {ex.Message}", null, ex);
        }
        finally
        {
            if (api is not null)
            {
                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during task-snapshot cleanup for task {TaskId}.", taskId); }
            }

            ReleaseComObject(comObject, $"task snapshot {taskId}");
        }
    }

    // Restores a complete task XML document after obtaining the required MOVEit checkout lock.
    public Task<MoveItTaskConfiguration> RestoreTaskConfigurationAsync(string taskId, string taskXml, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        if (string.IsNullOrWhiteSpace(taskXml)) throw new ArgumentException("Task XML is required.", nameof(taskXml));

        var normalizedTaskId = taskId.Trim();
        var validatedSnapshot = ParseTaskConfiguration(taskXml, normalizedTaskId, "configuration snapshot");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => RestoreTaskConfigurationCore(normalizedTaskId, validatedSnapshot), cancellationToken);
    }

    // Checks out the current task, applies the validated snapshot, and verifies the persisted result can be read back.
    private MoveItTaskConfiguration RestoreTaskConfigurationCore(string taskId, MoveItTaskConfiguration snapshot)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit task configuration restore requires Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before restoring task configuration.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        var checkedOut = false;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before using task configuration operations.");

            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? checkoutResult = api.CheckoutItem("Task", taskId);
            string currentXml = Convert.ToString(checkoutResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentXml))
                throw BuildApiException(comObject!, $"Unable to check out task '{taskId}' for configuration restore");
            checkedOut = true;
            _ = ParseTaskConfiguration(currentXml, taskId, "current checkout");

            object? modifyResult = api.ModifyItem("Task", taskId, snapshot.Xml, false);
            if (!ComResultSucceeded(modifyResult))
                throw BuildApiException(comObject!, $"MOVEit rejected the configuration restore for task '{taskId}'");

            object? savedXmlResult = api.GetItemXML("Task", taskId, false);
            string savedXml = Convert.ToString(savedXmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedXml))
                throw BuildApiException(comObject!, $"MOVEit restored task '{taskId}', but the saved task could not be read back for verification");

            var restored = ParseTaskConfiguration(savedXml, taskId, "restored task");
            _logger.LogInformation("Restored MOVEit task {TaskId} ({TaskName}) configuration through the Windows COM API.", taskId, restored.Name);
            return restored;
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM task-restore error: {ex.Message}", null, ex);
        }
        finally
        {
            Exception? releaseFailure = null;
            if (api is not null)
            {
                if (checkedOut) releaseFailure = ReleaseObjectCheckout(api, "Task", taskId);

                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during task-undo cleanup for task {TaskId}.", taskId); }
            }

            ReleaseComObject(comObject, $"task undo {taskId}");
            if (releaseFailure is not null) throw new ObjectCheckoutReleaseException("Task", taskId, releaseFailure);
        }
    }

    // Applies a caller-owned transformation to the current Task XML while MOVEit holds the checkout.
    // This avoids editing a stale configuration snapshot and reuses the same CheckoutItem/ModifyItem
    // lifecycle already used by MAUI's Task restore/undo functionality.
    public Task<MoveItTaskConfiguration> ModifyTaskConfigurationAsync(
        string taskId,
        Func<string, string> transform,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        ArgumentNullException.ThrowIfNull(transform);
        var normalizedTaskId = taskId.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => ModifyTaskConfigurationCore(normalizedTaskId, transform), cancellationToken);
    }

    private MoveItTaskConfiguration ModifyTaskConfigurationCore(string taskId, Func<string, string> transform)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit task configuration changes require Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before changing task configuration.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        var checkedOut = false;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before using task configuration operations.");

            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? checkoutResult = api.CheckoutItem("Task", taskId);
            string currentXml = Convert.ToString(checkoutResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentXml))
                throw BuildApiException(comObject!, $"Unable to check out task '{taskId}' for configuration update");
            checkedOut = true;
            _ = ParseTaskConfiguration(currentXml, taskId, "current checkout");

            var updatedXml = transform(currentXml) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(updatedXml))
                throw new InvalidOperationException("The Task configuration transformation returned an empty XML document.");
            _ = ParseTaskConfiguration(updatedXml, taskId, "updated configuration");

            object? modifyResult = api.ModifyItem("Task", taskId, updatedXml, false);
            if (!ComResultSucceeded(modifyResult))
                throw BuildApiException(comObject!, $"MOVEit rejected the configuration update for task '{taskId}'");

            object? savedXmlResult = api.GetItemXML("Task", taskId, false);
            string savedXml = Convert.ToString(savedXmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedXml))
                throw BuildApiException(comObject!, $"MOVEit updated task '{taskId}', but the saved task could not be read back for verification");

            var saved = ParseTaskConfiguration(savedXml, taskId, "updated task");
            _logger.LogInformation("Updated MOVEit task {TaskId} ({TaskName}) configuration through the Windows COM API.", taskId, saved.Name);
            return saved;
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException && ex is not InvalidOperationException && ex is not ArgumentException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM task-configuration error: {ex.Message}", null, ex);
        }
        finally
        {
            Exception? releaseFailure = null;
            if (api is not null)
            {
                if (checkedOut) releaseFailure = ReleaseObjectCheckout(api, "Task", taskId);

                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during task-configuration cleanup for task {TaskId}.", taskId); }
            }

            ReleaseComObject(comObject, $"task configuration update {taskId}");
            if (releaseFailure is not null) throw new ObjectCheckoutReleaseException("Task", taskId, releaseFailure);
        }
    }

    // Parses portable Task XML without requiring the source Task ID to match the destination.
    private static MoveItTaskConfiguration ParsePortableTaskConfiguration(string taskXml, string context)
    {
        XElement taskElement;
        try { taskElement = XElement.Parse(taskXml, LoadOptions.PreserveWhitespace); }
        catch (Exception ex) { throw new MoveItComException($"The {context} contains invalid task XML.", null, ex); }
        if (!taskElement.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase))
            throw new MoveItComException($"The {context} does not contain a MOVEit Task configuration.");
        var id = ((string?)taskElement.Attribute("ID") ?? string.Empty).Trim();
        var name = ((string?)taskElement.Attribute("Name") ?? string.Empty).Trim();
        if (name.Length == 0) throw new MoveItComException($"The {context} Task does not contain a name.");
        return new MoveItTaskConfiguration(id, name, taskElement.ToString(SaveOptions.DisableFormatting));
    }

    // Parses task XML and verifies that its embedded task ID matches the task being operated on.
    private static MoveItTaskConfiguration ParseTaskConfiguration(string taskXml, string expectedTaskId, string context)
    {
        XElement taskElement;
        try
        {
            taskElement = XElement.Parse(taskXml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new MoveItComException($"MOVEit returned invalid task XML while reading the {context} for task '{expectedTaskId}'.", null, ex);
        }

        if (!taskElement.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase))
            throw new MoveItComException($"MOVEit returned an unexpected configuration object while reading the {context} for task '{expectedTaskId}'.");

        var actualTaskId = ((string?)taskElement.Attribute("ID") ?? string.Empty).Trim();
        if (!string.Equals(actualTaskId, expectedTaskId, StringComparison.OrdinalIgnoreCase))
            throw new MoveItComException($"MOVEit returned task ID '{actualTaskId}' while MAUI expected task ID '{expectedTaskId}'. The operation was not submitted.");

        var taskName = ((string?)taskElement.Attribute("Name") ?? string.Empty).Trim();
        return new MoveItTaskConfiguration(actualTaskId, taskName, taskXml);
    }


    // Reads a complete custom Script XML snapshot for MAUI's one-level undo feature.
    public Task<MoveItScriptConfiguration> GetScriptConfigurationAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptId)) throw new ArgumentException("Script ID is required.", nameof(scriptId));
        var normalizedScriptId = scriptId.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => GetScriptConfigurationCore(normalizedScriptId), cancellationToken);
    }

    // Purpose: Returns script configuration core.
    private MoveItScriptConfiguration GetScriptConfigurationCore(string scriptId)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Script snapshots require Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before opening a Script editor.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null) throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before using Script configuration operations.");
            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost); api.SetUser(state.Username); api.SetPassword(state.Password);
            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult)) throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? xmlResult = api.GetItemXML("Script", scriptId, false);
            string scriptXml = Convert.ToString(xmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(scriptXml)) throw BuildApiException(comObject!, $"Unable to read Script '{scriptId}' for an undo snapshot");
            return ParseScriptConfiguration(scriptXml, scriptId, "snapshot");
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex) { throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex); }
        catch (Exception ex) when (ex is not MoveItAuthenticationException) { throw new MoveItComException($"Unexpected MOVEit COM Script-snapshot error: {ex.Message}", null, ex); }
        finally
        {
            if (api is not null) { try { api.Disconnect(); } catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Script-snapshot cleanup for Script {ScriptId}.", scriptId); } }
            ReleaseComObject(comObject, $"Script snapshot {scriptId}");
        }
    }

    // Restores a complete custom Script XML document after obtaining the required MOVEit checkout lock.
    public Task<MoveItScriptConfiguration> RestoreScriptConfigurationAsync(string scriptId, string scriptXml, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptId)) throw new ArgumentException("Script ID is required.", nameof(scriptId));
        if (string.IsNullOrWhiteSpace(scriptXml)) throw new ArgumentException("Script XML is required.", nameof(scriptXml));
        var normalizedScriptId = scriptId.Trim();
        var validatedSnapshot = ParseScriptConfiguration(scriptXml, normalizedScriptId, "configuration snapshot");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => RestoreScriptConfigurationCore(normalizedScriptId, validatedSnapshot), cancellationToken);
    }

    // Purpose: Restores script configuration core.
    private MoveItScriptConfiguration RestoreScriptConfigurationCore(string scriptId, MoveItScriptConfiguration snapshot)
    {
        if (!OperatingSystem.IsWindows()) throw new MoveItComException("MOVEit Script configuration restore requires Windows because the MOVEit Automation COM API is a Windows component.");
        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password)) throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before restoring Script configuration.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null; dynamic? api = null; var checkedOut = false;
        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null) throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before using Script configuration operations.");
            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost); api.SetUser(state.Username); api.SetPassword(state.Password);
            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult)) throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? checkoutResult = api.CheckoutItem("Script", scriptId);
            string currentXml = Convert.ToString(checkoutResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentXml)) throw BuildApiException(comObject!, $"Unable to check out Script '{scriptId}' for configuration restore");
            checkedOut = true;
            _ = ParseScriptConfiguration(currentXml, scriptId, "current checkout");
            if (ObjectEditVersion.IsRequired("EnvironmentCopy"))
                ObjectEditVersion.Verify("EnvironmentCopy", "Script|" + scriptId, MoveItAutomationUI.Features.EnvironmentCopy.EnvironmentCopyRules.RevisionXml(currentXml));

            object? modifyResult = api.ModifyItem("Script", scriptId, snapshot.Xml, false);
            if (!ComResultSucceeded(modifyResult)) throw BuildApiException(comObject!, $"MOVEit rejected the configuration restore for Script '{scriptId}'");

            object? savedXmlResult = api.GetItemXML("Script", scriptId, false);
            string savedXml = Convert.ToString(savedXmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedXml)) throw BuildApiException(comObject!, $"MOVEit restored Script '{scriptId}', but the saved Script could not be read back for verification");
            var restored = ParseScriptConfiguration(savedXml, scriptId, "restored Script");
            _logger.LogInformation("Restored MOVEit Script {ScriptId} ({ScriptName}) configuration through the Windows COM API.", scriptId, restored.Name);
            return restored;
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex) { throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex); }
        catch (Exception ex) when (ex is not MoveItAuthenticationException) { throw new MoveItComException($"Unexpected MOVEit COM Script-restore error: {ex.Message}", null, ex); }
        finally
        {
            Exception? releaseFailure = null;
            if (api is not null)
            {
                if (checkedOut) releaseFailure = ReleaseObjectCheckout(api, "Script", scriptId);

                try { api.Disconnect(); } catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Script-undo cleanup for Script {ScriptId}.", scriptId); }
            }
            ReleaseComObject(comObject, $"Script undo {scriptId}");
            if (releaseFailure is not null) throw new ObjectCheckoutReleaseException("Script", scriptId, releaseFailure);
        }
    }

    // Permanently deletes one custom Script through MOVEit's documented COM configuration API.
    public Task DeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptId)) throw new ArgumentException("Script ID is required.", nameof(scriptId));
        var normalizedScriptId = scriptId.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => DeleteScriptCore(normalizedScriptId), cancellationToken);
    }

    // Purpose: Deletes script core.
    private void DeleteScriptCore(string scriptId)
    {
        if (!OperatingSystem.IsWindows()) throw new MoveItComException("MOVEit Script deletion requires Windows because the MOVEit Automation COM API is a Windows component.");
        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password)) throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before deleting a Script.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null; dynamic? api = null;
        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null) throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before deleting Scripts.");
            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost); api.SetUser(state.Username); api.SetPassword(state.Password);
            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult)) throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? deleteResult = api.DeleteItem("Script", scriptId);
            if (!ComResultSucceeded(deleteResult)) throw BuildApiException(comObject!, $"MOVEit rejected deletion of Script '{scriptId}'");
            _logger.LogInformation("Deleted MOVEit custom Script {ScriptId} through the Windows COM API.", scriptId);
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex) { throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex); }
        catch (Exception ex) when (ex is not MoveItAuthenticationException) { throw new MoveItComException($"Unexpected MOVEit COM Script-deletion error: {ex.Message}", null, ex); }
        finally
        {
            if (api is not null) { try { api.Disconnect(); } catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Script-deletion cleanup for Script {ScriptId}.", scriptId); } }
            ReleaseComObject(comObject, $"Script delete {scriptId}");
        }
    }

    // Parses custom Script XML and verifies that its embedded Script ID matches the requested object.
    private static MoveItScriptConfiguration ParseScriptConfiguration(string scriptXml, string expectedScriptId, string context)
    {
        XElement scriptElement;
        try { scriptElement = XElement.Parse(scriptXml, LoadOptions.PreserveWhitespace); }
        catch (Exception ex) { throw new MoveItComException($"MOVEit returned invalid Script XML while reading the {context} for Script '{expectedScriptId}'.", null, ex); }

        if (!scriptElement.Name.LocalName.Equals("Script", StringComparison.OrdinalIgnoreCase))
            throw new MoveItComException($"MOVEit returned an unexpected configuration object while reading the {context} for Script '{expectedScriptId}'.");

        var actualScriptId = ((string?)scriptElement.Attribute("ID") ?? string.Empty).Trim();
        if (!string.Equals(actualScriptId, expectedScriptId, StringComparison.OrdinalIgnoreCase))
            throw new MoveItComException($"MOVEit returned Script ID '{actualScriptId}' while MAUI expected Script ID '{expectedScriptId}'. The operation was not submitted.");

        var scriptName = ((string?)scriptElement.Attribute("Name") ?? string.Empty).Trim();
        var language = ((string?)scriptElement.Attribute("Lang") ?? string.Empty).Trim();
        return new MoveItScriptConfiguration(actualScriptId, scriptName, language, scriptXml);
    }


    public Task<MoveItHostConfiguration> UpdateConfiguredHostAsync(string hostId, HostBasicUpdate update, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostId)) throw new ArgumentException("Host ID is required.", nameof(hostId));
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => UpdateConfiguredHostCore(hostId.Trim(), update), cancellationToken);
    }

    // One native Host update implementation for spreadsheet imports and future Host editors:
    // compose only explicit fields against the current checkout, then verify persisted identity.
    private MoveItHostConfiguration UpdateConfiguredHostCore(string hostId, HostBasicUpdate update)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Host editing requires Windows.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("MOVEit password is not available. Please sign in again.");

        object? comObject = null;
        dynamic? api = null;
        bool checkedOut = false;
        bool writeCommitted = false;
        bool completed = false;
        Exception? releaseFailure = null;
        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI")
                ?? throw new MoveItComException("MOVEit Automation COM API is not registered.");
            comObject = Activator.CreateInstance(apiType)
                ?? throw new MoveItComException("Unable to create the MOVEit Automation COM object.");
            api = comObject;
            api.SetHost(ResolveAutomationHost(state));
            api.SetUser(state.Username);
            api.SetPassword(state.Password);
            if (!ComResultSucceeded(api.Connect()))
                throw BuildApiException(comObject!, "COM login failed while updating Host");

            string currentXml = Convert.ToString(api.CheckoutItem("Host", hostId), CultureInfo.InvariantCulture) ?? string.Empty;
            if (currentXml.Length == 0)
                throw BuildApiException(comObject!, "Unable to check out the Host");
            checkedOut = true;
            var current = ParseHostConfiguration(currentXml, hostId, "current checkout");
            if (!current.TypeCode.Equals(update.HostType, StringComparison.OrdinalIgnoreCase)
                || !current.Name.Equals(update.Name, StringComparison.OrdinalIgnoreCase))
                throw new MoveItComException("Host identity or type changed since analysis. Reanalyze before updating.");

            string encryptedPassword = string.Empty;
            if (update.Fields.TryGetValue("PASSWORD", out var password) && !string.IsNullOrEmpty(password))
            {
                encryptedPassword = Convert.ToString(api.EncryptPW(password), CultureInfo.InvariantCulture) ?? string.Empty;
                if (encryptedPassword.Length == 0)
                    throw BuildApiException(comObject!, "MOVEit failed to encrypt the new Host password");
            }
            string keyId = string.Empty;
            if (update.HostType == "SSHFTP" && update.Fields.TryGetValue("SSHKEY", out var keyName)
                && !string.IsNullOrWhiteSpace(keyName))
            {
                string keyXml = Convert.ToString((object?)api.GetItemXML("SSHClientKey", keyName.Trim(), true), CultureInfo.InvariantCulture) ?? string.Empty;
                if (keyXml.Length == 0) throw new MoveItComException("The requested SSH client key is unavailable.");
                var keyRoot = XElement.Parse(keyXml);
                var key = keyRoot.Name.LocalName.Equals("SSHClientKey", StringComparison.OrdinalIgnoreCase)
                    ? keyRoot : keyRoot.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("SSHClientKey", StringComparison.OrdinalIgnoreCase));
                keyId = ((string?)key?.Attribute("ID") ?? string.Empty).Trim();
                if (keyId.Length == 0) throw new MoveItComException("The requested SSH client key has no usable ID.");
            }

            string updatedXml = HostBasicDefinitionProfile.ComposeUpdate(currentXml, update, encryptedPassword, keyId);
            if (!ComResultSucceeded(api.ModifyItem("Host", hostId, updatedXml, false)))
            {
                var failure = BuildApiException(comObject!, "MOVEit rejected the Host update");
                writeCommitted = failure.ErrorCode == 53; // Native UNC mount failure follows persistence.
                throw failure;
            }
            writeCommitted = true;

            string savedXml = Convert.ToString(api.GetItemXML("Host", hostId, false), CultureInfo.InvariantCulture) ?? string.Empty;
            if (savedXml.Length == 0)
                throw BuildApiException(comObject!, "Host update was submitted but readback is unavailable");
            var saved = ParseHostConfiguration(savedXml, hostId, "updated Host");
            if (!saved.TypeCode.Equals(update.HostType, StringComparison.OrdinalIgnoreCase)
                || !saved.Name.Equals(update.Name, StringComparison.OrdinalIgnoreCase))
                throw new MoveItComException("Host update was submitted but identity/type readback did not match.");
            HostDefinitionVerification.VerifyUpdate(currentXml, updatedXml, saved.Xml);
            completed = true;
            _logger.LogInformation("Updated MOVEit Host {HostId} ({HostName}) through the Windows COM API.", hostId, update.Name);
            return saved;
        }
        catch (Exception ex) when (writeCommitted)
        {
            throw new HostPartialSaveException("MOVEit accepted the Host write, but its saved configuration could not be verified. Review the Host before retrying.", ex, hostId);
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit Host update COM error 0x{ex.HResult:X8}.", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException("MOVEit Host update failed or could not be verified.", null, ex);
        }
        finally
        {
            if (api is not null)
            {
                if (checkedOut)
                    releaseFailure = ReleaseHostCheckout(api, hostId);
                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Host update cleanup."); }
            }
            ReleaseComObject(comObject, $"Host configured update {hostId}");
            HostDefinitionVerification.EnsureCheckoutReleased(completed, releaseFailure, hostId);
        }
    }

    // Updates the common Host Friendly Name and Description without altering type-specific settings.
    // MOVEit Host configuration uses a type-specific root, but every Host snapshot already exposes Name on
    // that root. Description is represented as Desc by the Host REST model; older/alternate XML shapes are
    // also honored when an existing Description/Desc element or attribute is present.
    public Task UpdateHostBasicDetailsAsync(
        string hostId,
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostId)) throw new ArgumentException("Host ID is required.", nameof(hostId));
        var normalizedHostId = hostId.Trim();
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Host Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("Host Name cannot exceed 254 characters.");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => UpdateHostBasicDetailsCore(normalizedHostId, normalizedName, description ?? string.Empty), cancellationToken);
    }

    // Purpose: Updates host basic details core.
    private void UpdateHostBasicDetailsCore(string hostId, string name, string description)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Host editing requires Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before editing a Host.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        var checkedOut = false;
        bool writeCommitted = false;
        bool completed = false;
        Exception? releaseFailure = null;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before editing Hosts.");

            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            string currentXml = Convert.ToString(api.CheckoutItem("Host", hostId), CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentXml))
                throw BuildApiException(comObject!, $"Unable to check out Host '{hostId}' for editing");
            checkedOut = true;

            var current = ParseHostConfiguration(currentXml, hostId, "current checkout");
            var root = XElement.Parse(current.Xml, LoadOptions.PreserveWhitespace);
            ObjectEditVersion.Verify("Host", hostId, ObjectEditVersion.Named((string?)root.Attribute("Name") ?? "", ObjectEditVersion.InformationValue(root, "Desc", "Description")));
            root.SetAttributeValue("Name", name);
            SetHostDescription(root, description);

            var updatedXml = root.ToString(SaveOptions.DisableFormatting);
            object? modifyResult = api.ModifyItem("Host", hostId, updatedXml, false);
            if (!ComResultSucceeded(modifyResult))
            {
                var failure = BuildApiException(comObject!, $"MOVEit rejected changes to Host '{hostId}'");
                writeCommitted = failure.ErrorCode == 53; // Native UNC mount failure follows persistence.
                throw failure;
            }
            writeCommitted = true;

            string savedXml = Convert.ToString(api.GetItemXML("Host", hostId, false), CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedXml))
                throw BuildApiException(comObject!, $"MOVEit updated Host '{hostId}', but the saved Host could not be read back for verification");
            var saved = ParseHostConfiguration(savedXml, hostId, "updated Host");
            if (!saved.Name.Equals(name, StringComparison.Ordinal))
                throw new MoveItComException($"MOVEit updated Host '{hostId}', but read-back returned Name='{saved.Name}' instead of '{name}'.");

            HostDefinitionVerification.VerifyUpdate(currentXml, updatedXml, savedXml);
            completed = true;
            _logger.LogInformation("Updated common MOVEit Host information for Host {HostId} ({HostName}).", hostId, name);
        }
        catch (Exception ex) when (writeCommitted)
        {
            throw new HostPartialSaveException("MOVEit accepted the Host write, but its saved configuration could not be verified. Review the Host before retrying.", ex, hostId);
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM Host-edit error: {ex.Message}", null, ex);
        }
        finally
        {
            if (api is not null)
            {
                if (checkedOut)
                    releaseFailure = ReleaseHostCheckout(api, hostId);
                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Host information-edit cleanup for Host {HostId}.", hostId); }
            }
            ReleaseComObject(comObject, $"Host information edit {hostId}");
            HostDefinitionVerification.EnsureCheckoutReleased(completed, releaseFailure, hostId);
        }
    }

    private Exception? ReleaseHostCheckout(object api, string hostId) => ReleaseObjectCheckout(api, "Host", hostId);

    private Exception? ReleaseObjectCheckout(object api, string objectType, string id)
    {
        try
        {
            dynamic nativeApi = api;
            if (ComResultSucceeded(nativeApi.ReleaseItem(objectType, id))) return null;
            var failure = BuildApiException(api, "MOVEit did not confirm checkout release");
            if (HostDefinitionVerification.IsCheckoutReleased(false, failure.ErrorCode)) return null;
            _logger.LogWarning("MOVEit did not confirm checkout release for {ObjectType} {ObjectId}.", objectType, id);
            return failure;
        }
        catch (Exception ex) { return ex; }
    }

    // Purpose: Sets host description.
    private static void SetHostDescription(XElement root, string description)
    {
        var descriptionAttribute = root.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("Desc", StringComparison.OrdinalIgnoreCase)
            || attribute.Name.LocalName.Equals("Description", StringComparison.OrdinalIgnoreCase));
        if (descriptionAttribute is not null)
        {
            descriptionAttribute.Value = description;
            return;
        }

        var descriptionElement = root.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("Description", StringComparison.OrdinalIgnoreCase)
            || element.Name.LocalName.Equals("Desc", StringComparison.OrdinalIgnoreCase));
        if (descriptionElement is not null)
        {
            descriptionElement.Value = description;
            return;
        }

        // The existing REST Host parser reads this common field as Desc; use that established MOVEit field
        // name only when the current native XML does not already provide another representation.
        root.SetAttributeValue("Desc", description);
    }

    // Reads a complete Host XML snapshot for MAUI's one-level undo feature without obtaining a checkout lock.
    public Task<MoveItHostConfiguration> GetHostConfigurationAsync(string hostId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostId)) throw new ArgumentException("Host ID is required.", nameof(hostId));
        var normalizedHostId = hostId.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => GetHostConfigurationCore(normalizedHostId), cancellationToken);
    }

    // Reads and validates the complete Host XML returned by GetItemXML.
    private MoveItHostConfiguration GetHostConfigurationCore(string hostId)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Host snapshots require Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before opening a Host editor.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before using Host configuration operations.");

            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? xmlResult = api.GetItemXML("Host", hostId, false);
            string hostXml = Convert.ToString(xmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(hostXml))
                throw BuildApiException(comObject!, $"Unable to read Host '{hostId}' for an undo snapshot");

            return ParseHostConfiguration(hostXml, hostId, "snapshot");
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM Host-snapshot error: {ex.Message}", null, ex);
        }
        finally
        {
            if (api is not null)
            {
                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Host-snapshot cleanup for Host {HostId}.", hostId); }
            }

            ReleaseComObject(comObject, $"Host snapshot {hostId}");
        }
    }

    // Restores a complete Host XML document after obtaining the required MOVEit checkout lock.
    public Task<MoveItHostConfiguration> RestoreHostConfigurationAsync(string hostId, string hostXml, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostId)) throw new ArgumentException("Host ID is required.", nameof(hostId));
        if (string.IsNullOrWhiteSpace(hostXml)) throw new ArgumentException("Host XML is required.", nameof(hostXml));

        var normalizedHostId = hostId.Trim();
        var validatedSnapshot = ParseHostConfiguration(hostXml, normalizedHostId, "configuration snapshot");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => RestoreHostConfigurationCore(normalizedHostId, validatedSnapshot), cancellationToken);
    }

    // Checks out the current Host, applies the validated snapshot, and verifies the persisted result can be read back.
    private MoveItHostConfiguration RestoreHostConfigurationCore(string hostId, MoveItHostConfiguration snapshot)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Host configuration restore requires Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before restoring Host configuration.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;
        var checkedOut = false;
        bool writeCommitted = false;
        bool completed = false;
        Exception? releaseFailure = null;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before using Host configuration operations.");

            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? checkoutResult = api.CheckoutItem("Host", hostId);
            string currentXml = Convert.ToString(checkoutResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentXml))
                throw BuildApiException(comObject!, $"Unable to check out Host '{hostId}' for configuration restore");
            checkedOut = true;
            _ = ParseHostConfiguration(currentXml, hostId, "current checkout");

            object? modifyResult = api.ModifyItem("Host", hostId, snapshot.Xml, false);
            if (!ComResultSucceeded(modifyResult))
            {
                var failure = BuildApiException(comObject!, $"MOVEit rejected the configuration restore for Host '{hostId}'");
                writeCommitted = failure.ErrorCode == 53; // Native UNC mount failure follows persistence.
                throw failure;
            }
            writeCommitted = true;

            object? savedXmlResult = api.GetItemXML("Host", hostId, false);
            string savedXml = Convert.ToString(savedXmlResult, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedXml))
                throw BuildApiException(comObject!, $"MOVEit restored Host '{hostId}', but the saved Host could not be read back for verification");

            var restored = ParseHostConfiguration(savedXml, hostId, "restored Host");
            _logger.LogInformation("Restored MOVEit Host {HostId} ({HostName}) configuration through the Windows COM API.", hostId, restored.Name);
            completed = true;
            return restored;
        }
        catch (Exception ex) when (writeCommitted)
        {
            throw new HostPartialSaveException("MOVEit accepted the Host write, but its saved configuration could not be verified. Review the Host before retrying.", ex, hostId);
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM Host-restore error: {ex.Message}", null, ex);
        }
        finally
        {
            if (api is not null)
            {
                if (checkedOut)
                    releaseFailure = ReleaseHostCheckout(api, hostId);
                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Host-undo cleanup for Host {HostId}.", hostId); }
            }

            ReleaseComObject(comObject, $"Host undo {hostId}");
            HostDefinitionVerification.EnsureCheckoutReleased(completed, releaseFailure, hostId);
        }
    }

    // Permanently deletes one Host through the MOVEit Windows API. DeleteItem does not require checkout.
    public Task DeleteHostAsync(string hostId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostId)) throw new ArgumentException("Host ID is required.", nameof(hostId));
        var normalizedHostId = hostId.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => DeleteHostCore(normalizedHostId), cancellationToken);
    }

    // Purpose: Deletes host core.
    private void DeleteHostCore(string hostId)
    {
        if (!OperatingSystem.IsWindows())
            throw new MoveItComException("MOVEit Host deletion requires Windows because the MOVEit Automation COM API is a Windows component.");

        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (string.IsNullOrWhiteSpace(state.Password))
            throw new MoveItAuthenticationException("The MOVEit password is no longer available in server memory. Please sign in again before deleting a Host.");

        var automationHost = ResolveAutomationHost(state);
        object? comObject = null;
        dynamic? api = null;

        try
        {
            var apiType = Type.GetTypeFromProgID("MICentralAPICOM.MICentralAPI");
            if (apiType is null)
                throw new MoveItComException("MICentralAPICOM.MICentralAPI is not registered on this web server. Install/register the MOVEit Automation Windows API components before deleting Hosts.");

            comObject = Activator.CreateInstance(apiType) ?? throw new MoveItComException("Unable to create the MOVEit Automation COM API object.");
            api = comObject;
            api.SetHost(automationHost);
            api.SetUser(state.Username);
            api.SetPassword(state.Password);

            object? connectResult = api.Connect();
            if (!ComResultSucceeded(connectResult))
                throw BuildApiException(comObject!, $"COM login to MOVEit Automation host '{automationHost}' failed");

            object? deleteResult = api.DeleteItem("Host", hostId);
            if (!ComResultSucceeded(deleteResult))
                throw BuildApiException(comObject!, $"MOVEit rejected deletion of Host '{hostId}'");

            _logger.LogInformation("Deleted MOVEit Host {HostId} through the Windows COM API.", hostId);
        }
        catch (MoveItComException) { throw; }
        catch (COMException ex)
        {
            throw new MoveItComException($"MOVEit COM error 0x{ex.HResult:X8}: {ex.Message}", null, ex);
        }
        catch (Exception ex) when (ex is not MoveItAuthenticationException)
        {
            throw new MoveItComException($"Unexpected MOVEit COM Host-deletion error: {ex.Message}", null, ex);
        }
        finally
        {
            if (api is not null)
            {
                try { api.Disconnect(); }
                catch (Exception ex) { _logger.LogDebug(ex, "MOVEit COM disconnect failed during Host-deletion cleanup for Host {HostId}.", hostId); }
            }

            ReleaseComObject(comObject, $"Host delete {hostId}");
        }
    }

    // Parses Host XML and verifies that its embedded ID matches the Host being operated on.
    // Host configuration roots are type-specific (FTP, SSHFTP, Share, etc.), so the root name is retained as TypeCode.
    private static MoveItHostConfiguration ParseHostConfiguration(string hostXml, string expectedHostId, string context)
    {
        XElement hostElement;
        try
        {
            hostElement = XElement.Parse(hostXml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new MoveItComException($"MOVEit returned invalid Host XML while reading the {context} for Host '{expectedHostId}'.", null, ex);
        }

        var actualHostId = ((string?)hostElement.Attribute("ID") ?? string.Empty).Trim();
        if (!string.Equals(actualHostId, expectedHostId, StringComparison.OrdinalIgnoreCase))
            throw new MoveItComException($"MOVEit returned Host ID '{actualHostId}' while MAUI expected Host ID '{expectedHostId}'. The operation was not submitted.");

        var hostName = ((string?)hostElement.Attribute("Name") ?? string.Empty).Trim();
        var typeCode = hostElement.Name.LocalName;
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new MoveItComException($"MOVEit returned an unexpected configuration object while reading the {context} for Host '{expectedHostId}'.");

        return new MoveItHostConfiguration(actualHostId, hostName, typeCode, hostXml);
    }


    // Purpose: Builds new date list XML.
    private static string BuildNewDateListXml(string id,string name,string description)
    { var root=new XElement("DateList",new XAttribute("ID",id),new XAttribute("Name",name),new XElement("Description",description??string.Empty));return root.ToString(SaveOptions.DisableFormatting); }

    // Purpose: Parses date list configuration.
    private static MoveItDateListConfiguration ParseDateListConfiguration(string xml,string expectedId,string context)
    {
        if(string.IsNullOrWhiteSpace(xml))throw new MoveItComException($"MOVEit returned empty Date List XML while reading the {context} for Date List '{expectedId}'.");XElement root;try{root=XElement.Parse(xml,LoadOptions.PreserveWhitespace);}catch(Exception ex){throw new MoveItComException($"MOVEit returned invalid Date List XML while reading the {context} for Date List '{expectedId}'.",null,ex);}
        if(!root.Name.LocalName.Equals("DateList",StringComparison.OrdinalIgnoreCase))throw new MoveItComException($"MOVEit returned an unexpected configuration object for Date List '{expectedId}'.");var id=((string?)root.Attribute("ID")??"").Trim();if(!id.Equals(expectedId,StringComparison.OrdinalIgnoreCase))throw new MoveItComException($"MOVEit returned Date List ID '{id}' while MAUI expected '{expectedId}'.");return new MoveItDateListConfiguration(id,((string?)root.Attribute("Name")??"").Trim(),xml);
    }

    // Builds the smallest custom Script shell needed to persist the object before native editing.
    // MOVEit can accept an empty Source through COM, but that produces an invalid/incomplete
    // custom Script for subsequent REST reads in Automation 2025.1. Seed the Source with a
    // harmless language-appropriate comment so the shell is syntactically valid until the
    // user replaces it in the native Script editor.
    private static string BuildNewScriptXml(string scriptId, string name, string language)
    {
        var normalizedLanguage = NormalizeScriptLanguage(language);
        var placeholderSource = normalizedLanguage.Equals("PS1", StringComparison.OrdinalIgnoreCase)
            ? "# MAUI placeholder - edit this script in the MOVEit Automation editor."
            : "' MAUI placeholder - edit this script in the MOVEit Automation editor.";

        var script = new XElement("Script",
            new XAttribute("ID", scriptId),
            new XAttribute("Name", name),
            new XAttribute("Lang", normalizedLanguage),
            new XElement("Description", string.Empty),
            new XElement("Source", placeholderSource));
        return script.ToString(SaveOptions.DisableFormatting);
    }

    // Normalizes the REST/native Script language identifiers supported by MAUI creation.
    private static string NormalizeScriptLanguage(string? language)
    {
        var value = (language ?? string.Empty).Trim();
        if (value.Equals("PS1", StringComparison.OrdinalIgnoreCase) || value.Equals("PowerShell", StringComparison.OrdinalIgnoreCase)) return "PS1";
        if (value.Equals("VBScript", StringComparison.OrdinalIgnoreCase) || value.Equals("VBS", StringComparison.OrdinalIgnoreCase)) return "VBScript";
        throw new InvalidOperationException("Script Language must be PowerShell or VBScript.");
    }

    // Builds the smallest documented Host-type XML skeleton needed to persist a new Host before native editing.
    private static string BuildNewHostXml(string hostId, string name, string hostType)
    {
        var host = hostType switch
        {
            "Share" => new XElement("Share",
                new XAttribute("ID", hostId), new XAttribute("Name", name),
                new XAttribute("DriveLetter", string.Empty), new XAttribute("UNC", string.Empty),
                new XAttribute("UseNotif", "0"), new XAttribute("DefRetryCount", "0"),
                new XAttribute("DefRetryTimeoutSecs", "30"), new XAttribute("DefRescanSecs", "0")),
            "siLock" => new XElement("siLock",
                new XAttribute("ID", hostId), new XAttribute("Name", name),
                new XAttribute("Host", string.Empty), new XAttribute("Port", "443"),
                new XAttribute("UseNotif", "0"), new XAttribute("ScriptPath", "siLockNet"),
                new XAttribute("Secure", "1"), new XAttribute("IgnoreCertProbs", "0"),
                new XAttribute("DefRetryCount", "0"), new XAttribute("DefRetryTimeoutSecs", "30"),
                new XAttribute("DefSort", "Filename,A"), new XAttribute("DefMxFi", "0"), new XAttribute("DefMxBy", "0")),
            "FTP" => new XElement("FTP",
                new XAttribute("ID", hostId), new XAttribute("Name", name),
                new XAttribute("Host", string.Empty), new XAttribute("Port", "21"),
                new XAttribute("NATUseServerIPForData", "1"), new XAttribute("DefAB", "binary"),
                new XAttribute("DefPassive", "1"), new XAttribute("DefBlindDL", "0"),
                new XAttribute("Secure", "none"), new XAttribute("DefRetryCount", "0"),
                new XAttribute("DefRetryTimeoutSecs", "30"), new XAttribute("DefRescanSecs", "0")),
            "SSHFTP" => new XElement("SSHFTP",
                new XAttribute("ID", hostId), new XAttribute("Name", name),
                new XAttribute("Host", string.Empty), new XAttribute("Port", "22"),
                new XAttribute("IgnoreCertProbs", "0"), new XAttribute("DefBlindDL", "0"),
                new XAttribute("DefRetryCount", "0"), new XAttribute("DefRetryTimeoutSecs", "30"),
                new XAttribute("DefRescanSecs", "0")),
            "POP3" => new XElement("POP3",
                new XAttribute("ID", hostId), new XAttribute("Name", name),
                new XAttribute("Host", string.Empty), new XAttribute("Port", "110"),
                new XAttribute("DefRetryCount", "0"), new XAttribute("DefRetryTimeoutSecs", "30")),
            "SMTP" => new XElement("SMTP",
                new XAttribute("ID", hostId), new XAttribute("Name", name),
                new XAttribute("Host", string.Empty), new XAttribute("Port", "25"),
                new XAttribute("DefAddressFrom", string.Empty),
                new XAttribute("DefRetryCount", "0"), new XAttribute("DefRetryTimeoutSecs", "30")),
            "S3" => new XElement("S3",
                new XAttribute("ID", hostId), new XAttribute("Name", name),
                new XAttribute("Secure", "1"), new XAttribute("DefBucket", string.Empty),
                new XAttribute("DefRetryCount", "0"), new XAttribute("DefRetryTimeoutSecs", "30"),
                new XAttribute("DefRescanSecs", "0")),
            "AzureBlob" => new XElement("AzureBlob",
                new XAttribute("DefContainer", string.Empty),
                new XAttribute("DefDLInteg", "never"),
                new XAttribute("ProxyType", "None"),
                new XAttribute("AdjDST", "0"),
                new XAttribute("DefMxBy", "0"),
                new XAttribute("DefMxFi", "0"),
                new XAttribute("DefRescanSecs", "0"),
                new XAttribute("DelOldStateDays", "0"),
                new XAttribute("Secure", "1"),
                new XAttribute("UseDefStateCaching", "1"),
                new XAttribute("DefConnTimeoutSecs", "60"),
                new XAttribute("DefDataTimeoutSecs", "60"),
                new XAttribute("DefUsername", string.Empty),
                new XAttribute("DefRetryCount", "0"),
                new XAttribute("DefRetryTimeoutSecs", "30"),
                new XAttribute("UsedByCount", "0"),
                new XAttribute("OverridesTotal", "0"),
                new XAttribute("OverridesNoPerm", "false"),
                new XAttribute("ID", hostId),
                new XAttribute("Name", name),
                new XElement("Desc", string.Empty)),
            "SharePoint" => new XElement("SharePoint",
                new XAttribute("TenantName", string.Empty),
                new XAttribute("Site", string.Empty),
                new XAttribute("SiteUrlPrefix", "/sites/"),
                new XAttribute("UsingAppAuthentication", "2"),
                new XAttribute("ClientID", string.Empty),
                new XAttribute("TenantID", string.Empty),
                new XAttribute("DefDocumentLibrary", string.Empty),
                new XAttribute("DefDocumentLibraryGuid", string.Empty),
                new XAttribute("DefClientCertStore", string.Empty),
                new XAttribute("DefClientCertSubject", string.Empty),
                new XAttribute("DefClientCertSHA1", string.Empty),
                new XAttribute("ProxyType", "None"),
                new XAttribute("AdjDST", "0"),
                new XAttribute("DefMxBy", "0"),
                new XAttribute("DefMxFi", "0"),
                new XAttribute("DefRescanSecs", "0"),
                new XAttribute("DelOldStateDays", "0"),
                new XAttribute("Secure", "1"),
                new XAttribute("UseDefStateCaching", "1"),
                new XAttribute("DefConnTimeoutSecs", "60"),
                new XAttribute("DefDataTimeoutSecs", "60"),
                new XAttribute("DefRetryCount", "0"),
                new XAttribute("DefRetryTimeoutSecs", "30"),
                new XAttribute("UsedByCount", "0"),
                new XAttribute("OverridesTotal", "0"),
                new XAttribute("OverridesNoPerm", "false"),
                new XAttribute("ID", hostId),
                new XAttribute("Name", name),
                new XElement("Desc", string.Empty)),
            "GoogleStorage" => new XElement("GoogleStorage",
                new XAttribute("DefBucket", string.Empty),
                new XAttribute("UseOAuth", "0"),
                new XAttribute("ProxyType", "None"),
                new XAttribute("AdjDST", "0"),
                new XAttribute("DefMxBy", "0"),
                new XAttribute("DefMxFi", "0"),
                new XAttribute("DefRescanSecs", "0"),
                new XAttribute("DelOldStateDays", "0"),
                new XAttribute("Secure", "1"),
                new XAttribute("UseDefStateCaching", "1"),
                new XAttribute("DefConnTimeoutSecs", "60"),
                new XAttribute("DefDataTimeoutSecs", "60"),
                new XAttribute("DefUsername", string.Empty),
                new XAttribute("DefRetryCount", "0"),
                new XAttribute("DefRetryTimeoutSecs", "30"),
                new XAttribute("UsedByCount", "0"),
                new XAttribute("OverridesTotal", "0"),
                new XAttribute("OverridesNoPerm", "false"),
                new XAttribute("ID", hostId),
                new XAttribute("Name", name),
                new XElement("Desc", string.Empty)),
            _ => throw new InvalidOperationException($"Host Type '{hostType}' is not supported for MAUI creation.")
        };
        return host.ToString(SaveOptions.DisableFormatting);
    }

    // Verifies that MOVEit persisted the Host ID, Name, and type requested by MAUI.
    private static void VerifyCreatedHost(string hostXml, string expectedId, string expectedName, string expectedHostType)
    {
        XElement host;
        try { host = XElement.Parse(hostXml, LoadOptions.PreserveWhitespace); }
        catch (Exception ex) { throw new MoveItComException($"MOVEit created Host ID {expectedId}, but returned invalid Host XML during verification.", null, ex); }

        var actualId = ((string?)host.Attribute("ID") ?? string.Empty).Trim();
        var actualName = ((string?)host.Attribute("Name") ?? string.Empty).Trim();
        var actualType = NormalizeHostType(host.Name.LocalName);
        if (!string.Equals(actualId, expectedId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actualName, expectedName, StringComparison.Ordinal) ||
            !string.Equals(actualType, expectedHostType, StringComparison.OrdinalIgnoreCase))
        {
            throw new MoveItComException(
                $"MOVEit created Host ID {expectedId}, but read-back verification did not match the requested Host. " +
                $"Returned ID='{actualId}', Name='{actualName}', Type='{host.Name.LocalName}'.");
        }
    }

    // Normalizes the Host type identifiers supported by the New Host dialog.
    private static string NormalizeHostType(string? hostType)
    {
        var value = (hostType ?? string.Empty).Trim();
        foreach (var known in new[] { "Share", "siLock", "FTP", "SSHFTP", "POP3", "SMTP", "S3", "AzureBlob", "SharePoint", "GoogleStorage" })
            if (value.Equals(known, StringComparison.OrdinalIgnoreCase)) return known;
        throw new InvalidOperationException("Host Type must be UNC Share, MOVEit Transfer, FTP/FTPS, SSH/SFTP, POP3, SMTP, Amazon S3, Azure Blob, SharePoint, or Google Storage.");
    }

    // Converts one Host type code into the label returned to the New Host dialog.
    private static string FriendlyHostType(string hostType) => NormalizeHostType(hostType) switch
    {
        "Share" => "UNC Share",
        "siLock" => "MOVEit Transfer",
        "FTP" => "FTP/FTPS",
        "SSHFTP" => "SSH/SFTP",
        "POP3" => "POP3 (Incoming Email)",
        "SMTP" => "SMTP (Outgoing Email)",
        "S3" => "Amazon S3",
        "AzureBlob" => "Azure Blob",
        "SharePoint" => "SharePoint",
        "GoogleStorage" => "Google Storage",
        _ => hostType
    };

    // Builds the task-type-specific persistent XML skeleton. New tasks are disabled/unscheduled and
    // intentionally contain no configured steps; the native Web Admin editor completes the configuration.
    // Purpose: Build the minimal task XML required for a new task type.
    private static string BuildNewTaskXml(string taskId, string name, string taskType)
    {
        XElement taskElement;
        switch (taskType)
        {
            case "Advanced":
                taskElement = BuildAdvancedTaskElement(taskId, name);
                break;
            case "Synchronization":
                taskElement = BuildSynchronizationTaskElement(taskId, name);
                break;
            default:
                taskElement = BuildTraditionalTaskElement(taskId, name);
                break;
        }

        return taskElement.ToString(SaveOptions.DisableFormatting);
    }

    // Purpose: Build traditional task element.
    private static XElement BuildTraditionalTaskElement(string taskId, string name)
        => BuildBaseTaskElement(taskId, name, null);

    // Purpose: Build advanced task element.
    private static XElement BuildAdvancedTaskElement(string taskId, string name)
        => BuildBaseTaskElement(taskId, name, "COS");

    // Purpose: Build synchronization task element.
    private static XElement BuildSynchronizationTaskElement(string taskId, string name)
    {
        var task = BuildBaseTaskElement(taskId, name, "Sy");
        // MOVEit's documented 12-hour (43200-second) default timestamp tolerance for initial synchronization comparison.
        task.SetAttributeValue("SameSecs", "43200");
        return task;
    }

    // Purpose: Build base task element.
    private static XElement BuildBaseTaskElement(string taskId, string name, string? taskTypeCode)
    {
        var task = new XElement("Task",
            new XAttribute("ID", taskId),
            new XAttribute("Name", name),
            new XAttribute("Active", "0"),
            new XAttribute("NextEID", "1"),
            new XAttribute("AR", "0"),
            new XAttribute("CacheNames", "random"),
            new XAttribute("UseDefStateCaching", "1"));

        if (!string.IsNullOrWhiteSpace(taskTypeCode))
        {
            task.SetAttributeValue("TT", taskTypeCode);
        }

        task.Add(new XElement("Schedules"));
        return task;
    }

    // Purpose: Verify created task.
    private static void VerifyCreatedTask(string taskXml, string expectedId, string expectedName, string expectedTaskType)
    {
        if (string.IsNullOrWhiteSpace(taskXml))
        {
            throw new MoveItComException(
                $"MOVEit reported that task '{expectedName}' was created, but the task could not be read back for verification. Task ID: {expectedId}.");
        }

        XElement taskElement;
        try
        {
            taskElement = XElement.Parse(taskXml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new MoveItComException(
                $"MOVEit created task ID {expectedId}, but returned invalid task XML during verification.",
                null,
                ex);
        }

        if (!taskElement.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase))
        {
            throw new MoveItComException($"MOVEit created task ID {expectedId}, but returned an unexpected configuration object during verification.");
        }

        var actualId = (string?)taskElement.Attribute("ID") ?? string.Empty;
        var actualName = (string?)taskElement.Attribute("Name") ?? string.Empty;
        var actualType = TaskTypeFromCode((string?)taskElement.Attribute("TT"));

        if (!string.Equals(actualId, expectedId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actualName, expectedName, StringComparison.Ordinal) ||
            !string.Equals(actualType, expectedTaskType, StringComparison.OrdinalIgnoreCase))
        {
            throw new MoveItComException(
                $"MOVEit created task ID {expectedId}, but read-back verification did not match the requested task. " +
                $"Returned ID='{actualId}', Name='{actualName}', Type='{actualType}'.");
        }
    }

    // Purpose: Normalize task type.
    private static string NormalizeTaskType(string? taskType)
    {
        var value = (taskType ?? string.Empty).Trim();
        if (value.Equals("Traditional", StringComparison.OrdinalIgnoreCase)) return "Traditional";
        if (value.Equals("Advanced", StringComparison.OrdinalIgnoreCase)) return "Advanced";
        if (value.Equals("Synchronization", StringComparison.OrdinalIgnoreCase) || value.Equals("Sync", StringComparison.OrdinalIgnoreCase)) return "Synchronization";
        throw new InvalidOperationException("Task Type must be Traditional, Advanced, or Synchronization.");
    }

    // Purpose: Translate the MOVEit task type code into the MAUI task type name.
    private static string TaskTypeFromCode(string? taskTypeCode)
    {
        if (string.Equals(taskTypeCode, "COS", StringComparison.OrdinalIgnoreCase)) return "Advanced";
        if (string.Equals(taskTypeCode, "Sy", StringComparison.OrdinalIgnoreCase)) return "Synchronization";
        return "Traditional";
    }

    // Normalizes late-bound COM TRUE/FALSE values because Automation may expose them as bools or numeric VARIANT values.
    // Normalize the result instead of casting directly to bool so both representations are accepted.
    // Purpose: Normalize COM return values into a consistent success decision.
    private static bool ComResultSucceeded(object? result)
    {
        if (result is null)
        {
            return false;
        }

        if (result is bool boolResult)
        {
            return boolResult;
        }

        if (result is string stringResult)
        {
            if (bool.TryParse(stringResult, out var parsedBoolean))
            {
                return parsedBoolean;
            }

            if (long.TryParse(stringResult, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedNumber))
            {
                return parsedNumber != 0;
            }
        }

        try
        {
            return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture) != 0;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new MoveItComException(
                $"MOVEit COM returned an unexpected success/failure value of type '{result.GetType().FullName}' with value '{result}'.",
                null,
                ex);
        }
    }

    // Preserves the installed MOVEit XML representation because task information has historically been stored as child values.
    // representation the installed version returns; only create the canonical child name when no matching node exists.
    // Purpose: Update one Task Information XML value while preserving compatible aliases.
    private static void SetTaskInformationValue(XElement taskElement, IReadOnlyList<string> aliases, string canonicalName, string value)
    {
        var infoContainer = taskElement.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("Info", StringComparison.OrdinalIgnoreCase));
        var searchRoot = infoContainer ?? taskElement;

        var existingElement = searchRoot.Elements().FirstOrDefault(element => aliases.Any(alias => element.Name.LocalName.Equals(alias, StringComparison.OrdinalIgnoreCase)));
        if (existingElement is not null)
        {
            existingElement.Value = value;
            return;
        }

        // Preserves an installed-version representation that stores the value as an attribute rather than
        // converting it to a child element. This is defensive; current REST represents these values under Info.
        var existingAttribute = searchRoot.Attributes().FirstOrDefault(attribute => aliases.Any(alias => attribute.Name.LocalName.Equals(alias, StringComparison.OrdinalIgnoreCase)));
        if (existingAttribute is not null)
        {
            existingAttribute.Value = value;
            return;
        }

        searchRoot.Add(new XElement(searchRoot.Name.Namespace + canonicalName, value));
    }

    // An explicit COM host override is used when configured; otherwise the selected Web Admin URL host is used.
    private static string ResolveAutomationHost(MoveItSessionState state)
    {
        if (!string.IsNullOrWhiteSpace(state.ServerHost)) return state.ServerHost.Trim();
        if (Uri.TryCreate(state.WebAdminUrl, UriKind.Absolute, out var webAdminUri) && !string.IsNullOrWhiteSpace(webAdminUri.Host))
            return webAdminUri.Host;
        throw new MoveItComException("The selected server does not provide a usable Automation host for the Windows COM API.");
    }

    // Adds MOVEit's own API error code/description to failures returned as false/blank by the COM interface.
    // COM values are immediately copied into strongly typed .NET values so dynamic dispatch cannot escape this boundary.
    // Purpose: Convert MOVEit COM error details into a managed exception.
    private static MoveItComException BuildApiException(object apiObject, string operation)
    {
        dynamic api = apiObject;
        int? code = null;
        string description = string.Empty;
        Exception? diagnosticException = null;

        try
        {
            object? rawCode = api.GetErrorCode();
            if (rawCode is not null)
            {
                code = Convert.ToInt32(rawCode, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex)
        {
            diagnosticException = ex;
        }

        try
        {
            object? rawDescription = api.GetErrorDescription();
            description = Convert.ToString(rawDescription, CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch (Exception ex)
        {
            diagnosticException ??= ex;
        }

        int? effectiveCode = code is 0 ? null : code;
        if (effectiveCode.HasValue || !string.IsNullOrWhiteSpace(description))
        {
            string codeText = effectiveCode.HasValue ? $"{effectiveCode.Value}: " : string.Empty;
            return new MoveItComException(
                $"{operation}: {codeText}{description}".TrimEnd(),
                effectiveCode,
                diagnosticException);
        }

        return new MoveItComException(
            operation + ". MOVEit did not return additional COM error information.",
            null,
            diagnosticException);
    }

    // The COM object is owned exclusively within this client call, and cleanup must never hide the actual MOVEit operation error.
    private void ReleaseComObject(object? comObject, string operation)
    {
        if (comObject is null || !OperatingSystem.IsWindows()) return;

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release MOVEit COM object after {Operation}.", operation);
        }
    }
}

