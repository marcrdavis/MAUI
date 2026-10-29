/*
 * Author: Marc R. Davis (marcdavis@comcast.net)
 * Project: MAUI - MOVEit Automation UI
 * RC2 Traditional Task editor shell with Source, Process, Destination, Schedule and Next Action editing framework.
 */

(() => {
    function initialize(options = {}) {
        const dialog = document.querySelector('[data-traditional-task-editor-dialog]');
        if (!dialog) return null;

        const config = options.configElement || document.querySelector('[data-task-page-config]');
        if (!config) return null;

        const onPreflight = typeof options.onPreflight === 'function' ? options.onPreflight : null;
        const onRunNow = typeof options.onRunNow === 'function' ? options.onRunNow : null;
        const onExport = typeof options.onExport === 'function' ? options.onExport : null;
        const onDeleteTask = typeof options.onDeleteTask === 'function' ? options.onDeleteTask : null;
        const onCloneTask = typeof options.onCloneTask === 'function' ? options.onCloneTask : null;
        const onSaveNotificationTemplate = typeof options.onSaveNotificationTemplate === 'function' ? options.onSaveNotificationTemplate : null;
        const canSaveTemplates = document.documentElement.dataset.canSaveTemplates === 'true';
        const onClosed = typeof options.onClosed === 'function' ? options.onClosed : null;
        const antiForgeryToken = options.antiForgeryToken || '';
        const loading = dialog.querySelector('[data-traditional-task-editor-loading]');
        const error = dialog.querySelector('[data-traditional-task-editor-error]');
        const content = dialog.querySelector('[data-traditional-task-editor-content]');
        const title = dialog.querySelector('[data-traditional-task-editor-title]');
        const description = dialog.querySelector('[data-traditional-task-editor-description]');
        const undoButton = dialog.querySelector('[data-traditional-task-editor-undo]');
        const autoSaveToggle = dialog.querySelector('[data-traditional-task-editor-auto-save]');
        const actionsButton = dialog.querySelector('[data-traditional-task-editor-actions]');
        const actionsMenu = dialog.querySelector('[data-traditional-task-editor-actions-menu]');
        const runNowAction = dialog.querySelector('[data-traditional-task-editor-run-now]');
        const runAsOfAction = dialog.querySelector('[data-traditional-task-editor-run-as-of]');
        let runAsOfBusy = false;
        const taskParametersButton = dialog.querySelector('[data-traditional-task-editor-task-parameters]');
        const preflightAction = dialog.querySelector('[data-traditional-task-editor-preflight]');
        const exportAction = dialog.querySelector('[data-traditional-task-editor-export]');
        const deleteAction = dialog.querySelector('[data-traditional-task-editor-delete]');
        const cloneAction = dialog.querySelector('[data-traditional-task-editor-clone]');
        const createAdvancedAction = dialog.querySelector('[data-traditional-task-editor-create-advanced]');
        const templateSelections = new Map();
        const transferExceptionsButton = dialog.querySelector('[data-traditional-task-editor-transfer-exceptions]');
        const addStepButton = dialog.querySelector('[data-traditional-task-editor-add-step]');
        const addStepMenu = dialog.querySelector('[data-traditional-task-editor-add-step-menu]');
        const addSourceButton = dialog.querySelector('[data-traditional-task-editor-add-source]');
        const addProcessButton = dialog.querySelector('[data-traditional-task-editor-add-process]');
        const addDestinationButton = dialog.querySelector('[data-traditional-task-editor-add-destination]');
        const addNextActionButton = dialog.querySelector('[data-traditional-task-editor-add-next-action]');
        const addNextActionMenu = dialog.querySelector('[data-traditional-task-editor-add-next-action-menu]');
        const addEmailActionButton = dialog.querySelector('[data-traditional-task-editor-add-email-action]');
        const addRunTaskActionButton = dialog.querySelector('[data-traditional-task-editor-add-run-task-action]');
        const closeButton = dialog.querySelector('[data-traditional-task-editor-close]');
        const cancelButton = dialog.querySelector('[data-traditional-task-editor-cancel]');
        const saveButton = dialog.querySelector('[data-traditional-task-editor-save]');
        const reloadButton = dialog.querySelector('[data-traditional-task-editor-reload]');
        const overrideButton = dialog.querySelector('[data-traditional-task-editor-override]');
        const dirtyState = dialog.querySelector('[data-traditional-task-editor-dirty-state]');
        const tabs = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-tab]'));
        const panels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-panel]'));
        const railSections = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-section]'));
        const workspacePanels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-workspace]'));
        const metadataFields = dialog.querySelector('[data-traditional-task-editor-metadata-fields]');
        const auditHost = dialog.querySelector('[data-traditional-task-editor-audit]');
        const folderSelect = dialog.querySelector('[data-traditional-task-editor-folder]');
        const logsLoading = dialog.querySelector('[data-traditional-task-editor-logs-loading]');
        const logsError = dialog.querySelector('[data-traditional-task-editor-logs-error]');
        const logsEmpty = dialog.querySelector('[data-traditional-task-editor-logs-empty]');
        const logsList = dialog.querySelector('[data-traditional-task-editor-logs-list]');
        const logsRows = dialog.querySelector('[data-traditional-task-editor-log-rows]');

        // Keep client-side host behavior keyed to the same profile identifiers advertised by the
        // server capability catalog. Future Host support should add a verified profile implementation
        // rather than scattering raw MOVEit Host type checks through the editor.
        const HOST_WRITE_PROFILE_LOCAL = 'filesystem-v1';
        const HOST_WRITE_PROFILE_SHARE = 'share-v1';
        const HOST_WRITE_PROFILE_MOVEIT_TRANSFER = 'silock-v1';
        const HOST_WRITE_PROFILE_FTP = 'ftp-v1';
        const HOST_WRITE_PROFILE_SFTP = 'sshftp-v1';
        const HOST_WRITE_PROFILE_S3 = 's3-v1';
        const HOST_WRITE_PROFILE_AZURE_BLOB = 'azureblob-v1';
        const HOST_WRITE_PROFILE_SHAREPOINT = 'sharepoint-v1';
        const HOST_WRITE_PROFILE_GOOGLE_STORAGE = 'googlestorage-v1';
        const HOST_WRITE_PROFILE_POP3 = 'pop3-v1';
        const HOST_WRITE_PROFILE_SMTP = 'smtp-v1';

        const SOURCE_WRITE_PROFILES = new Set([
            'sync-local',
            HOST_WRITE_PROFILE_LOCAL,
            HOST_WRITE_PROFILE_SHARE,
            HOST_WRITE_PROFILE_MOVEIT_TRANSFER,
            HOST_WRITE_PROFILE_FTP,
            HOST_WRITE_PROFILE_SFTP,
            HOST_WRITE_PROFILE_S3,
            HOST_WRITE_PROFILE_AZURE_BLOB,
            HOST_WRITE_PROFILE_SHAREPOINT,
            HOST_WRITE_PROFILE_GOOGLE_STORAGE,
            HOST_WRITE_PROFILE_POP3
        ]);
        const DESTINATION_WRITE_PROFILES = new Set([
            HOST_WRITE_PROFILE_LOCAL,
            HOST_WRITE_PROFILE_SHARE,
            HOST_WRITE_PROFILE_MOVEIT_TRANSFER,
            HOST_WRITE_PROFILE_FTP,
            HOST_WRITE_PROFILE_SFTP,
            HOST_WRITE_PROFILE_S3,
            HOST_WRITE_PROFILE_AZURE_BLOB,
            HOST_WRITE_PROFILE_SHAREPOINT,
            HOST_WRITE_PROFILE_GOOGLE_STORAGE,
            HOST_WRITE_PROFILE_SMTP
        ]);
        const BROWSE_PROFILES = new Set([
            HOST_WRITE_PROFILE_LOCAL,
            HOST_WRITE_PROFILE_SHARE,
            HOST_WRITE_PROFILE_MOVEIT_TRANSFER,
            HOST_WRITE_PROFILE_FTP,
            HOST_WRITE_PROFILE_SFTP,
            HOST_WRITE_PROFILE_S3,
            HOST_WRITE_PROFILE_AZURE_BLOB,
            HOST_WRITE_PROFILE_SHAREPOINT,
            HOST_WRITE_PROFILE_GOOGLE_STORAGE
        ]);

        const stepItems = dialog.querySelector('[data-traditional-task-editor-step-items]');
        const stepEmpty = dialog.querySelector('[data-traditional-task-editor-step-empty]');
        const stepDetail = dialog.querySelector('[data-traditional-task-editor-step-detail]');
        const stepKicker = dialog.querySelector('[data-traditional-task-editor-step-kicker]');
        const stepTitle = dialog.querySelector('[data-traditional-task-editor-step-title]');
        const stepSummary = dialog.querySelector('[data-traditional-task-editor-step-summary]');
        const stepActionsButton = dialog.querySelector('[data-traditional-task-editor-step-actions]');
        const stepActionsMenu = dialog.querySelector('[data-traditional-task-editor-step-actions-menu]');
        const stepActionButtons = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-step-action]'));
        const genericStep = dialog.querySelector('[data-traditional-task-editor-generic-step]');
        const genericStepMessage = dialog.querySelector('[data-traditional-task-editor-generic-step-message]');
        const sourceEditor = dialog.querySelector('[data-traditional-task-editor-source]');
        const sourceHostTypeFilter = dialog.querySelector('[data-traditional-task-editor-source-host-type-filter]');
        const sourceHostSelect = dialog.querySelector('[data-traditional-task-editor-source-host]');
        const sourceHostHelp = dialog.querySelector('[data-traditional-task-editor-source-host-help]');
        const sourceHostType = dialog.querySelector('[data-traditional-task-editor-source-host-type]');
        const sourceBrowseButton = dialog.querySelector('[data-traditional-task-editor-source-browse]');
        const sourceBrowseWarning = dialog.querySelector('[data-traditional-task-editor-source-browse-warning]');
        const sourceBucketBrowseButton = dialog.querySelector('[data-traditional-task-editor-source-bucket-browse]');
        const sourceBucketHelp = dialog.querySelector('[data-traditional-task-editor-source-bucket-help]');
        const sourceContainerBrowseButton = dialog.querySelector('[data-traditional-task-editor-source-container-browse]');
        const sourceContainerHelp = dialog.querySelector('[data-traditional-task-editor-source-container-help]');
        const sourceDocumentLibraryBrowseButton = dialog.querySelector('[data-traditional-task-editor-source-document-library-browse]');
        const sourceDocumentLibraryHelp = dialog.querySelector('[data-traditional-task-editor-source-document-library-help]');
        const sourceCredentialUserLabel = dialog.querySelector('[data-traditional-task-editor-source-credential-user-label]');
        const sourceCredentialSecretLabel = dialog.querySelector('[data-traditional-task-editor-source-credential-password-label]');
        const sourcePasswordHelp = dialog.querySelector('[data-traditional-task-editor-source-password-help]');
        const sourceClientCertHelp = dialog.querySelector('[data-traditional-task-editor-source-client-cert-help]');
        const sourceClientKeyHelp = dialog.querySelector('[data-traditional-task-editor-source-client-key-help]');
        const specialFiltersButton = dialog.querySelector('[data-traditional-task-editor-special-filters]');
        const specialFilterStatus = dialog.querySelector('[data-traditional-task-editor-special-filter-status]');
        const specialFiltersDialog = document.querySelector('[data-traditional-task-editor-special-filters-dialog]');
        const specialFiltersSubtitle = specialFiltersDialog?.querySelector('[data-traditional-task-editor-special-filters-subtitle]');
        const specialFiltersError = specialFiltersDialog?.querySelector('[data-traditional-task-editor-special-filters-error]');
        const specialFiltersReadonly = specialFiltersDialog?.querySelector('[data-traditional-task-editor-special-filters-readonly]');
        const specialFilterMatchControls = Array.from(specialFiltersDialog?.querySelectorAll('[data-traditional-task-editor-special-filter-match]') || []);
        const specialFilterRows = specialFiltersDialog?.querySelector('[data-traditional-task-editor-special-filter-rows]');
        const specialFilterMore = specialFiltersDialog?.querySelector('[data-traditional-task-editor-special-filter-more]');
        const specialFiltersApply = specialFiltersDialog?.querySelector('[data-traditional-task-editor-special-filters-apply]');
        const specialFiltersCancelButtons = Array.from(specialFiltersDialog?.querySelectorAll('[data-traditional-task-editor-special-filters-cancel]') || []);
        const sourceTabs = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-source-tab]'));
        const sourcePanels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-source-panel]'));
        const hostTabs = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-host-tab]'));
        const hostPanels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-host-panel]'));
        const hostOverrideSupported = dialog.querySelector('[data-traditional-task-editor-host-override-supported]');
        const hostOverrideUnsupported = dialog.querySelector('[data-traditional-task-editor-host-override-unsupported]');
        const renameOptions = dialog.querySelector('[data-traditional-task-editor-rename-options]');
        const processEditor = dialog.querySelector('[data-traditional-task-editor-process]');
        const processTabs = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-process-tab]'));
        const processPanels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-process-panel]'));
        const processScriptSelect = dialog.querySelector('[data-traditional-task-editor-process-script]');
        const processScriptHelp = dialog.querySelector('[data-traditional-task-editor-process-script-help]');
        const processParameterTable = dialog.querySelector('[data-traditional-task-editor-process-parameter-table]');
        const processParameterRows = dialog.querySelector('[data-traditional-task-editor-process-parameter-rows]');
        const processParametersEmpty = dialog.querySelector('[data-traditional-task-editor-process-parameters-empty]');
        const processAddParameter = dialog.querySelector('[data-traditional-task-editor-process-add-parameter]');
        const destinationEditor = dialog.querySelector('[data-traditional-task-editor-destination]');
        const destinationTabs = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-destination-tab]'));
        const destinationPanels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-destination-panel]'));
        const destinationHostTypeFilter = dialog.querySelector('[data-traditional-task-editor-destination-host-type-filter]');
        const destinationHostSelect = dialog.querySelector('[data-traditional-task-editor-destination-host]');
        const destinationHostHelp = dialog.querySelector('[data-traditional-task-editor-destination-host-help]');
        const destinationHostType = dialog.querySelector('[data-traditional-task-editor-destination-host-type]');
        const destinationBrowseButton = dialog.querySelector('[data-traditional-task-editor-destination-browse]');
        const destinationBrowseWarning = dialog.querySelector('[data-traditional-task-editor-destination-browse-warning]');
        const destinationBucketBrowseButton = dialog.querySelector('[data-traditional-task-editor-destination-bucket-browse]');
        const destinationBucketHelp = dialog.querySelector('[data-traditional-task-editor-destination-bucket-help]');
        const destinationContainerBrowseButton = dialog.querySelector('[data-traditional-task-editor-destination-container-browse]');
        const destinationContainerHelp = dialog.querySelector('[data-traditional-task-editor-destination-container-help]');
        const destinationDocumentLibraryBrowseButton = dialog.querySelector('[data-traditional-task-editor-destination-document-library-browse]');
        const destinationDocumentLibraryHelp = dialog.querySelector('[data-traditional-task-editor-destination-document-library-help]');
        const destinationCredentialUserLabel = dialog.querySelector('[data-traditional-task-editor-destination-credential-user-label]');
        const destinationCredentialSecretLabel = dialog.querySelector('[data-traditional-task-editor-destination-credential-password-label]');
        const destinationPasswordHelp = dialog.querySelector('[data-traditional-task-editor-destination-password-help]');
        const destinationClientCertHelp = dialog.querySelector('[data-traditional-task-editor-destination-client-cert-help]');
        const destinationClientKeyHelp = dialog.querySelector('[data-traditional-task-editor-destination-client-key-help]');
        const destinationFileName = dialog.querySelector('[data-traditional-task-editor-destination-file-name]');
        const destinationHostTabs = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-destination-host-tab]'));
        const destinationHostPanels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-destination-host-panel]'));
        const destinationHostOverrideSupported = dialog.querySelector('[data-traditional-task-editor-destination-host-override-supported]');
        const destinationHostOverrideUnsupported = dialog.querySelector('[data-traditional-task-editor-destination-host-override-unsupported]');
        const fieldMacroSelects = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-field-macro]'));
        const parametersDialog = document.querySelector('[data-traditional-task-editor-parameters-dialog]');
        const parameterRows = parametersDialog?.querySelector('[data-traditional-task-editor-parameter-rows]');
        const parametersEmpty = parametersDialog?.querySelector('[data-traditional-task-editor-parameters-empty]');
        const parameterTable = parametersDialog?.querySelector('[data-traditional-task-editor-parameter-table]');
        const addParameterButton = parametersDialog?.querySelector('[data-traditional-task-editor-add-parameter]');
        const addParameterDialog = document.querySelector('[data-traditional-task-editor-add-parameter-dialog]');
        const taskParameterDialogTitle = addParameterDialog?.querySelector('[data-traditional-task-editor-task-parameter-title]');
        const taskParameterDialogSubtitle = addParameterDialog?.querySelector('[data-traditional-task-editor-task-parameter-subtitle]');
        const taskParameterName = addParameterDialog?.querySelector('[data-traditional-task-editor-new-parameter-name]');
        const taskParameterValue = addParameterDialog?.querySelector('[data-traditional-task-editor-new-parameter-value]');
        const taskParameterSecure = addParameterDialog?.querySelector('[data-traditional-task-editor-new-parameter-secure]');
        const taskParameterSave = addParameterDialog?.querySelector('[data-traditional-task-editor-task-parameter-save]');
        const taskParameterError = addParameterDialog?.querySelector('[data-traditional-task-editor-task-parameter-error]');
        const transferExceptionsDialog = document.querySelector('[data-traditional-task-editor-transfer-exceptions-dialog]');
        const transferExceptionsLoading = transferExceptionsDialog?.querySelector('[data-traditional-task-editor-transfer-exceptions-loading]');
        const transferExceptionsError = transferExceptionsDialog?.querySelector('[data-traditional-task-editor-transfer-exceptions-error]');
        const transferExceptionsRows = transferExceptionsDialog?.querySelector('[data-traditional-task-editor-transfer-exceptions-rows]');
        const transferExceptionsEmpty = transferExceptionsDialog?.querySelector('[data-traditional-task-editor-transfer-exceptions-empty]');
        const transferExceptionsStatus = transferExceptionsDialog?.querySelector('[data-traditional-task-editor-transfer-exceptions-status]');
        const sourceTimestampsDialog = document.querySelector('[data-traditional-task-editor-source-timestamps-dialog]');
        const sourceTimestampsHost = sourceTimestampsDialog?.querySelector('[data-traditional-task-editor-source-timestamps-host]');
        const sourceTimestampsLoading = sourceTimestampsDialog?.querySelector('[data-traditional-task-editor-source-timestamps-loading]');
        const sourceTimestampsError = sourceTimestampsDialog?.querySelector('[data-traditional-task-editor-source-timestamps-error]');
        const sourceTimestampsRows = sourceTimestampsDialog?.querySelector('[data-traditional-task-editor-source-timestamp-rows]');
        const sourceTimestampsEmpty = sourceTimestampsDialog?.querySelector('[data-traditional-task-editor-source-timestamp-empty]');
        const sourceTimestampsStatus = sourceTimestampsDialog?.querySelector('[data-traditional-task-editor-source-timestamps-status]');
        const sourceTimestampAdd = sourceTimestampsDialog?.querySelector('[data-traditional-task-editor-source-timestamp-add]');
        const sourceTimestampEditDialog = document.querySelector('[data-traditional-task-editor-source-timestamp-edit-dialog]');
        const sourceTimestampEditTitle = sourceTimestampEditDialog?.querySelector('[data-traditional-task-editor-source-timestamp-edit-title]');
        const sourceTimestampEditHost = sourceTimestampEditDialog?.querySelector('[data-traditional-task-editor-source-timestamp-edit-host]');
        const sourceTimestampEditError = sourceTimestampEditDialog?.querySelector('[data-traditional-task-editor-source-timestamp-edit-error]');
        const sourceTimestampPath = sourceTimestampEditDialog?.querySelector('[data-traditional-task-editor-source-timestamp-path]');
        const sourceTimestampMask = sourceTimestampEditDialog?.querySelector('[data-traditional-task-editor-source-timestamp-mask]');
        const sourceTimestampValue = sourceTimestampEditDialog?.querySelector('[data-traditional-task-editor-source-timestamp-value]');
        const sourceTimestampHelp = sourceTimestampEditDialog?.querySelector('[data-traditional-task-editor-source-timestamp-help]');
        const sourceTimestampSave = sourceTimestampEditDialog?.querySelector('[data-traditional-task-editor-source-timestamp-save]');
        const sourceTimestampEditCancelButtons = Array.from(sourceTimestampEditDialog?.querySelectorAll('[data-traditional-task-editor-source-timestamp-edit-cancel]') || []);
        const processParameterDialog = document.querySelector('[data-traditional-task-editor-process-parameter-dialog]');
        const processParameterTitle = processParameterDialog?.querySelector('[data-traditional-task-editor-process-parameter-title]');
        const processParameterSubtitle = processParameterDialog?.querySelector('[data-traditional-task-editor-process-parameter-subtitle]');
        const processParameterName = processParameterDialog?.querySelector('[data-traditional-task-editor-process-parameter-name]');
        const processParameterValue = processParameterDialog?.querySelector('[data-traditional-task-editor-process-parameter-value]');
        const processParameterSecure = processParameterDialog?.querySelector('[data-traditional-task-editor-process-parameter-secure]');
        const processParameterError = processParameterDialog?.querySelector('[data-traditional-task-editor-process-parameter-error]');
        const processParameterSave = processParameterDialog?.querySelector('[data-traditional-task-editor-process-parameter-save]');
        const processParameterReset = processParameterDialog?.querySelector('[data-traditional-task-editor-process-parameter-reset]');
        const processParameterCancelButtons = Array.from(processParameterDialog?.querySelectorAll('[data-traditional-task-editor-process-parameter-cancel]') || []);
        const nextActionParameterDialog = document.querySelector('[data-traditional-task-editor-next-action-parameter-dialog]');
        const nextActionParameterTitle = nextActionParameterDialog?.querySelector('[data-traditional-task-editor-next-action-parameter-title]');
        const nextActionParameterSubtitle = nextActionParameterDialog?.querySelector('[data-traditional-task-editor-next-action-parameter-subtitle]');
        const nextActionParameterName = nextActionParameterDialog?.querySelector('[data-traditional-task-editor-next-action-parameter-name]');
        const nextActionParameterValue = nextActionParameterDialog?.querySelector('[data-traditional-task-editor-next-action-parameter-value]');
        const nextActionParameterSecure = nextActionParameterDialog?.querySelector('[data-traditional-task-editor-next-action-parameter-secure]');
        const nextActionParameterError = nextActionParameterDialog?.querySelector('[data-traditional-task-editor-next-action-parameter-error]');
        const nextActionParameterSave = nextActionParameterDialog?.querySelector('[data-traditional-task-editor-next-action-parameter-save]');
        const nextActionParameterCancelButtons = Array.from(nextActionParameterDialog?.querySelectorAll('[data-traditional-task-editor-next-action-parameter-cancel]') || []);
        let folderBrowser = null;

        const scheduleItems = dialog.querySelector('[data-traditional-task-editor-schedule-items]');
        const scheduleEmpty = dialog.querySelector('[data-traditional-task-editor-schedule-empty]');
        const scheduleDetail = dialog.querySelector('[data-traditional-task-editor-schedule-detail]');
        const scheduleKicker = dialog.querySelector('[data-traditional-task-editor-schedule-kicker]');
        const scheduleTitle = dialog.querySelector('[data-traditional-task-editor-schedule-title]');
        const scheduleHeadingSummary = dialog.querySelector('[data-traditional-task-editor-schedule-heading-summary]');
        const scheduleSummary = dialog.querySelector('[data-traditional-task-editor-schedule-summary]');
        const scheduleReadOnlyNotice = dialog.querySelector('[data-traditional-task-editor-schedule-readonly]');
        const scheduleActionsButton = dialog.querySelector('[data-traditional-task-editor-schedule-actions]');
        const scheduleActionsMenu = dialog.querySelector('[data-traditional-task-editor-schedule-actions-menu]');
        const scheduleActionButtons = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-schedule-action]'));
        const scheduleAddButton = dialog.querySelector('[data-traditional-task-editor-add-schedule]');
        const scheduleTabs = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-schedule-tab]'));
        const schedulePanels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-schedule-panel]'));
        const scheduleDayModeButtons = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-schedule-day-mode]'));
        const scheduleDayPanels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-schedule-day-panel]'));
        const scheduleDateListRows = dialog.querySelector('[data-traditional-task-editor-schedule-date-list-rows]');
        const scheduleAddDateListButton = dialog.querySelector('[data-traditional-task-editor-add-schedule-date-list]');
        const scheduleIntervals = dialog.querySelector('[data-traditional-task-editor-schedule-intervals]');
        const scheduleAddTimeButton = dialog.querySelector('[data-traditional-task-editor-add-schedule-time]');

        const nextActionItems = dialog.querySelector('[data-traditional-task-editor-next-action-items]');
        const nextActionEmpty = dialog.querySelector('[data-traditional-task-editor-next-action-empty]');
        const nextActionDetail = dialog.querySelector('[data-traditional-task-editor-next-action-detail]');
        const nextActionKicker = dialog.querySelector('[data-traditional-task-editor-next-action-kicker]');
        const nextActionTitle = dialog.querySelector('[data-traditional-task-editor-next-action-title]');
        const nextActionSummary = dialog.querySelector('[data-traditional-task-editor-next-action-summary]');
        const nextActionActionsButton = dialog.querySelector('[data-traditional-task-editor-next-action-actions]');
        const nextActionActionsMenu = dialog.querySelector('[data-traditional-task-editor-next-action-actions-menu]');
        const nextActionActionButtons = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-next-action-action]'));
        const nextActionTabs = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-next-action-tab]'));
        const nextActionPanels = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-next-action-panel]'));
        const nextActionEmailGeneral = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-email-general]'));
        const nextActionRunTaskGeneral = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-run-task-general]'));
        const nextActionSmtpHost = dialog.querySelector('[data-traditional-task-editor-next-action-smtp-host]');
        const nextActionTask = dialog.querySelector('[data-traditional-task-editor-next-action-task]');
        const nextActionFormat = dialog.querySelector('[data-traditional-task-editor-next-action-format]');
        const nextActionSubject = dialog.querySelector('[data-traditional-task-editor-next-action-subject]');
        const nextActionHtmlToolbar = dialog.querySelector('[data-traditional-task-editor-next-action-html-toolbar]');
        const nextActionHtmlEditor = dialog.querySelector('[data-traditional-task-editor-next-action-html-editor]');
        const nextActionHtmlSource = dialog.querySelector('[data-traditional-task-editor-next-action-html-source]');
        const nextActionTextEditor = dialog.querySelector('[data-traditional-task-editor-next-action-text-editor]');
        const nextActionHtmlToggle = dialog.querySelector('[data-traditional-task-editor-next-action-html-toggle]');
        const nextActionLinkButton = dialog.querySelector('[data-traditional-task-editor-next-action-link]');
        const nextActionLinkDialog = document.querySelector('[data-traditional-task-editor-next-action-link-dialog]');
        const nextActionLinkUrl = nextActionLinkDialog?.querySelector('[data-traditional-task-editor-next-action-link-url]');
        const nextActionLinkError = nextActionLinkDialog?.querySelector('[data-traditional-task-editor-next-action-link-error]');
        const nextActionLinkConfirm = nextActionLinkDialog?.querySelector('[data-traditional-task-editor-next-action-link-confirm]');
        const nextActionLinkCancelButtons = Array.from(nextActionLinkDialog?.querySelectorAll('[data-traditional-task-editor-next-action-link-cancel]') || []);
        const nextActionSubjectMacro = dialog.querySelector('[data-traditional-task-editor-next-action-subject-macro]');
        const nextActionBodyMacro = dialog.querySelector('[data-traditional-task-editor-next-action-body-macro]');
        const nextActionTemplateButton = dialog.querySelector('[data-traditional-task-editor-next-action-template-button]');
        const nextActionTemplateMenu = dialog.querySelector('[data-traditional-task-editor-next-action-template-menu]');
        const nextActionSaveTemplate = dialog.querySelector('[data-traditional-task-editor-next-action-save-template]');
        const destinationSmtpRecipientMacro = dialog.querySelector('[data-traditional-task-editor-destination-smtp-recipient-macro]');
        const destinationSmtpSubjectMacro = dialog.querySelector('[data-traditional-task-editor-destination-smtp-subject-macro]');
        const destinationSmtpBodyMacro = dialog.querySelector('[data-traditional-task-editor-destination-smtp-body-macro]');
        const destinationSmtpHtmlToolbar = dialog.querySelector('[data-traditional-task-editor-destination-smtp-html-toolbar]');
        const destinationSmtpHtmlEditor = dialog.querySelector('[data-traditional-task-editor-destination-smtp-html-editor]');
        const destinationSmtpHtmlSource = dialog.querySelector('[data-traditional-task-editor-destination-smtp-html-source]');
        const destinationSmtpTextEditor = dialog.querySelector('[data-traditional-task-editor-destination-smtp-text-editor]');
        const destinationSmtpHtmlToggle = dialog.querySelector('[data-traditional-task-editor-destination-smtp-html-toggle]');
        const destinationSmtpLinkButton = dialog.querySelector('[data-traditional-task-editor-destination-smtp-link]');
        const destinationSmtpTemplateButton = dialog.querySelector('[data-traditional-task-editor-destination-smtp-template-button]');
        const destinationSmtpTemplateMenu = dialog.querySelector('[data-traditional-task-editor-destination-smtp-template-menu]');
        const destinationSmtpSaveTemplate = dialog.querySelector('[data-traditional-task-editor-destination-smtp-save-template]');
        const nextActionParameterTable = dialog.querySelector('[data-traditional-task-editor-next-action-parameter-table]');
        const nextActionParameterRows = dialog.querySelector('[data-traditional-task-editor-next-action-parameter-rows]');
        const nextActionParametersEmpty = dialog.querySelector('[data-traditional-task-editor-next-action-parameters-empty]');
        const nextActionAddParameter = dialog.querySelector('[data-traditional-task-editor-next-action-add-parameter]');
        const stateCacheModeControls = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-state-cache-mode]'));
        const stateCacheTimeField = dialog.querySelector('[data-traditional-task-editor-state-cache-time]');
        const stateCacheForValue = dialog.querySelector('[data-traditional-task-editor-state-cache-for-value]');
        const stateCacheUnit = dialog.querySelector('[data-traditional-task-editor-state-cache-unit]');
        const useDefaultStateCache = dialog.querySelector('[data-traditional-task-editor-use-default-state-cache]');
        const stateCacheSettings = dialog.querySelector('[data-traditional-task-editor-state-cache-settings]');
        const useGlobalTaskLogRetention = dialog.querySelector('[data-traditional-task-editor-use-global-task-log-retention]');
        const taskLogRetentionSettings = dialog.querySelector('[data-traditional-task-editor-task-log-retention-settings]');
        const taskLogsToKeepInput = dialog.querySelector('[data-traditional-task-editor-logs-to-keep]');
        const taskLogRetentionWarning = dialog.querySelector('[data-traditional-task-editor-task-log-retention-warning]');

        let activeTaskId = '';
        let editVersion = '';
        let activeTaskName = '';
        let activeTaskType = 'Traditional';
        const isSyncTask = () => activeTaskType.toLowerCase() === 'synchronization';
        const isAdvancedTask = () => activeTaskType.toLowerCase() === 'advanced';
        let advancedEditor = null;
        let exportingFlow = false;
        const saveFlowAction = dialog.querySelector('[data-advanced-save-flow]');
        const syncTraditionalFields = ['NewFilesOnly','Unzip','RetryIfNoFiles','DeleteOrig','RenameTo','DelRename','UseDefWinCopyFileAPI','UseWinCopyFileAPI'];
        let canAlterTask = true;
        let canDeleteTask = true, canCreateTask = true;
        let currentCanRun = false;
        let saveOutcomeUncertain = false, checkoutConflict = false, manualRecoverySave = false;
        let conversionOutcomeUncertain = false;
        let dirty = false;
        let logsRequest = null;
        let logsGeneration = 0;
        let loadingShell = false;
        let saving = false;
        let activeSavePromise = null;
        let autoSave = false;
        let autoSaveTimer = 0;
        let editingHost = false;
        const editHostButtons = Array.from(dialog.querySelectorAll('[data-traditional-task-editor-edit-host]'));
        const isMoveItAdmin = document.documentElement.dataset.moveitAdmin === 'true';
        let autoSaveBlockedSignature = '';
        let serverUndoCaptured = false;
        let footerSaveError = '';
        let taskParameterEditName = '';
        let nextActionParameterEditName = '';
        let lastUndo = null;
        let taskSteps = [];
        let schedules = [];
        let nextActions = [];
        let hostCapabilities = new Map();
        let sourceHosts = [];
        let destinationHosts = [];
        let scriptOptions = [];
        let smtpHosts = [];
        let taskOptions = [];
        let dateListOptions = [];
        let tlsCertificates = [];
        let sshClientKeys = [];
        let notificationTemplates = [];
        let taskParameters = [];
        let transferExceptions = [];
        let transferExceptionsBusy = false;
        let sourceTimestamps = [];
        let sourceTimestampsBusy = false;
        let sourceTimestampContext = null;
        let sourceTimestampEditIndex = -1;
        let activeStepKey = '';
        let activeScheduleKey = '';
        let activeNextActionKey = '';
        let sourceTabName = 'general';
        let hostTabName = 'timeouts';
        let processTabName = 'general';
        let destinationTabName = 'general';
        let destinationHostTabName = 'timeouts';
        let temporaryStepSequence = 0;
        let temporaryScheduleSequence = 0;
        let temporaryScheduleIntervalSequence = 0;
        let temporaryScheduleDateListSequence = 0;
        let temporaryNextActionSequence = 0;
        let stepsBaseline = '';
        let schedulesBaseline = '';
        let schedulesBaselineHadNativeMulti = false;
        let nextActionsBaseline = '';
        let taskParametersBaseline = '';
        const stepBaselineModels = new Map();
        let scheduleTabName = 'days';
        let scheduleDayMode = 'week';
        let nextActionTabName = 'general';
        let nextActionHtmlSourceMode = false;
        let nextActionHtmlRange = null;
        let destinationSmtpHtmlSourceMode = false;
        let destinationSmtpHtmlRange = null;
        let emailLinkEditorContext = '';
        const sourceHostTypeFilters = new Map();
        const destinationHostTypeFilters = new Map();
        let processParameterEditName = '';
        let folderBrowserTarget = null;
        let specialFilterDraft = null;
        const syncPreviewDialog = document.querySelector('[data-sync-preview-dialog]');
        const syncInitialDialog = document.querySelector('[data-sync-initial-dialog]');
        let syncActionBusy = false;
        let syncPreviewBusy = false;
        let syncPreviewData = null;
        let syncPreviewRequest = null;
        let syncPreviewSequence = 0;
        const baseline = new Map();
        const stableValues = new Map();
        const sourceStableValues = new Map();
        const processStableValues = new Map();
        const destinationStableValues = new Map();
        const nextActionStableValues = new Map();

        const checkout = window.MauiObjectEditLease?.create({ type: 'Task', token: antiForgeryToken,
            busy: () => saving || syncActionBusy || runAsOfBusy,
            onLost: message => { checkoutConflict = true; footerSaveError = message; window.clearTimeout(autoSaveTimer); updateDirtyState(); } });

        const scriptGuidance = window.MauiTaskScriptGuidance?.create({ root: dialog,
            url: () => dialog.dataset.scriptMetadataUrl,
            scripts: () => scriptOptions, process: () => activeStep()?.process,
            processes: () => taskSteps.filter(step => step.kind === 'Process').map(step => step.process),
            advanced: isAdvancedTask, taskParameters: () => taskParameters, canEdit: () => canAlterTask,
            editTask: openTaskParameterEditor, editProcess: openProcessParameterEditor,
            taskDialog: openTaskParameters, processTab: () => setProcessTab('parameters'),
            changed: () => { renderTaskParameters(); const process = activeStep()?.process; if (process) renderProcessParameters(process); }
        });

        function endpoint(name) {
            return dialog.dataset[name] || config.dataset[name] || '';
        }

        async function fetchJson(url, requestOptions = {}) {
            if (requestOptions.method && requestOptions.method.toUpperCase() !== 'GET') checkout?.assert();
            const headers = new Headers(requestOptions.headers || {});
            headers.set('Accept', 'application/json');
            if (checkout?.id) headers.set('X-MAUI-Edit-Lease', checkout.id);
            if (requestOptions.body) headers.set('Content-Type', 'application/json');
            if (antiForgeryToken && requestOptions.method && requestOptions.method.toUpperCase() !== 'GET') {
                headers.set('RequestVerificationToken', antiForgeryToken);
            }
            const response = await fetch(url, { ...requestOptions, headers, cache: 'no-store' });
            if (response.status === 401) {
                window.location.replace(endpoint('loginUrl') || window.mauiAppUrl('/Account/SessionExpired'));
                throw new Error('MOVEit session expired.');
            }
            const contentType = response.headers.get('content-type') || '';
            const payload = contentType.includes('application/json') ? await response.json() : null;
            if (!response.ok) {
                if (payload?.leaseLost || payload?.checkoutConflict) checkout?.invalidate(payload.message);
                const error = new Error(payload?.message || `Request failed (${response.status}).`);
                error.payload = payload;
                error.status = response.status;
                throw error;
            }
            return payload;
        }

        function supportsTaskType(value) {
            return ['traditional', 'synchronization', 'advanced'].includes(String(value || '').trim().toLowerCase());
        }

        function isTraditionalTaskType(value) { return String(value || '').trim().toLowerCase() === 'traditional'; }

        async function open(taskId, taskName = '', taskType = '') {
            if (saving || activeSavePromise || syncActionBusy || runAsOfBusy || editingHost) return false;
            const normalizedId = String(taskId || '').trim();
            if (!normalizedId || loadingShell) return false;

            await checkout?.close();
            reset();
            activeTaskId = normalizedId;
            activeTaskName = String(taskName || normalizedId).trim();
            title.textContent = activeTaskName;
            description.textContent = 'Loading task…';
            // Apply known type geometry before the loading dialog's first visible frame.
            // Callers without a type wait for the authoritative shell before showing it.
            const hintedType = ({traditional:'Traditional',advanced:'Advanced',cos:'Advanced',synchronization:'Synchronization',sync:'Synchronization',sy:'Synchronization'})[String(taskType || '').trim().toLowerCase()];
            if (hintedType) { activeTaskType = hintedType; configureTaskKind({}); dialog.showModal(); }
            loadingShell = true;

            try {
                const baseUrl = endpoint('traditionalTaskEditorShellUrl');
                if (!baseUrl) throw new Error('The Traditional Task Editor endpoint is not configured.');
                const url = new URL(baseUrl, window.location.href);
                url.searchParams.set('id', normalizedId);
                let data = await fetchJson(url.toString());
                if (hintedType && !dialog.open) return false;
                if (data?.checkoutObjectsForDurationOfEdit && !checkout) throw new Error('Checkout support is unavailable. Reload MAUI before editing.');
                if (data?.task?.canAlter !== false && await checkout?.open(normalizedId, true, data?.checkoutObjectsForDurationOfEdit))
                    data = await fetchJson(url.toString());
                if (hintedType && !dialog.open) return false;
                populate(data || {});
                if (!dialog.open) dialog.showModal();
                if (isAdvancedTask()) advancedEditor?.refresh();
                return true;
            } catch (requestError) {
                if (hintedType && !dialog.open) return false;
                if (!dialog.open) dialog.showModal();
                await checkout?.close();
                showError(requestError?.message || 'Unable to load the Traditional Task Editor.');
                return false;
            } finally {
                loadingShell = false;
                updateEditHostButtons();
            }
        }

        function configureTaskKind(task) {
            const sync = isSyncTask(), advanced = isAdvancedTask();
            dialog.classList.toggle('is-advanced-task', advanced);
            dialog.querySelector('[data-advanced-mobile]').hidden=!advanced;
            dialog.querySelector('[data-traditional-task-editor-section=next-actions]').hidden = advanced;
            dialog.querySelector('[data-traditional-task-editor-section=steps] .traditional-task-editor-rail-items').hidden = advanced;
            dialog.classList.toggle('is-sync-task', sync);
            setText('[data-task-editor-kind-label]', sync ? 'Synchronization Task Editor' : advanced ? 'Advanced Task Editor' : 'Traditional Task Editor');
            setText('[data-task-editor-folders-label]', sync ? 'Sync Folders' : 'Steps');
            dialog.querySelectorAll('[data-sync-only]').forEach(element => { element.hidden = !sync; });
            if (addStepButton) {
                addStepButton.replaceChildren(document.createTextNode(sync ? '+ Add Sync Folder' : '+ Add Step '));
                if (!sync) { const caret = document.createElement('span'); caret.className = 'button-caret'; caret.setAttribute('aria-hidden', 'true'); caret.textContent = '⌄'; addStepButton.append(caret); }
                addStepButton.setAttribute('aria-haspopup', sync ? 'false' : 'menu');
            }
            if (stepActionsButton) stepActionsButton.hidden = false;
            const emptyHeading = stepEmpty?.querySelector('h3');
            const emptyDescription = stepEmpty?.querySelector('p');
            if (emptyHeading) emptyHeading.textContent = sync ? 'Define Sync Folder A and Folder B' : 'Select or add a task step';
            if (emptyDescription) emptyDescription.textContent = sync ? 'Add two folders, then configure the schedule and Next Actions.' : 'Select an existing step from the left or add a new step.';
            [addProcessButton, addDestinationButton, transferExceptionsButton].forEach(element => { if (element) element.hidden = sync; });
            if (createAdvancedAction) createAdvancedAction.hidden = sync || advanced;
            if (!sync) {
                sourceTabs.forEach(tab => { if (tab.dataset.traditionalTaskEditorSourceTab === 'after-transfer') tab.hidden = false; });
                ['collectOnlyNewFiles','expandZipFiles','retryIfNoFilesFound','overrideWindowsCopyFileApi','useWindowsCopyFileApi'].forEach(field => dialog.querySelectorAll(`[data-source-field="${field}"]`).forEach(control => { const row = control.closest('label'); if (row) row.hidden = false; }));
                sourceTabs.forEach(tab => { if (tab.dataset.traditionalTaskEditorSourceTab === 'host-overrides') tab.hidden = false; });
                if (sourceHostTypeFilter) sourceHostTypeFilter.disabled = false;
                return;
            }
            const folders = task.synchronization?.folders || [];
            const a = folders[0], b = folders[1];
            setValue('[data-sync-direction]', a?.from === 1 && a?.to === 1 && b?.from === 1 && b?.to === 1 ? 'bidirectional' : 'unidirectional');
            setValue('[data-sync-deletes]', String(a?.deletes ?? 1));
            const comparisons = dialog.querySelector('[data-sync-comparisons]');
            if (comparisons) {
                comparisons.checked = a?.lowerCaseComparisons === 0;
                comparisons.dataset.syncMixedChecked = String(comparisons.checked);
                comparisons.indeterminate = folders.length === 2 && a?.lowerCaseComparisons !== b?.lowerCaseComparisons;
            }
            const seconds = task.synchronization?.sameSeconds ?? 43200;
            setValue('[data-editor-field="syncInitialRun"]', seconds < 0 ? String(seconds) : 'custom');
            setValue('[data-editor-field="syncSameSeconds"]', String(seconds >= 0 ? seconds : 43200));
            refreshSyncSettingsPresentation();
        }

        function applySyncSourcePresentation(source) {
            if (!isSyncTask()) return;
            const local = source.hostTypeCode === 'FileSystem';
            sourceTabs.forEach(tab => { if (tab.dataset.traditionalTaskEditorSourceTab === 'host-overrides') tab.hidden = local; });
            if (local && sourceTabName === 'host-overrides') setSourceTab('general');
            if (sourceHostSelect) sourceHostSelect.disabled = false;
            if (sourceHostTypeFilter) sourceHostTypeFilter.disabled = false;
            const fields = ['collectOnlyNewFiles','expandZipFiles','retryIfNoFilesFound','overrideWindowsCopyFileApi','useWindowsCopyFileApi'];
            fields.forEach(field => dialog.querySelectorAll(`[data-source-field="${field}"]`).forEach(control => {
                control.disabled = true;
                const row = control.closest('label');
                if (row) row.hidden = true;
            }));
            sourceTabs.forEach(tab => { if (tab.dataset.traditionalTaskEditorSourceTab === 'after-transfer') tab.hidden = true; });
            if (sourceTabName === 'after-transfer') setSourceTab('general');
            setSourceControlValue('overridePreserveFileTimestamp', source.overridePreserveFileTimestamp);
            setSourceControlValue('preserveFileTimestamp', source.preserveFileTimestamp);
            const timestamp = dialog.querySelector('[data-source-field="preserveFileTimestamp"]');
            if (timestamp) timestamp.disabled = !source.overridePreserveFileTimestamp;
        }

        function reset() {
            scriptGuidance?.reset();
            templateSelections.clear();
            syncPreviewRequest?.abort(); syncPreviewSequence++;
            syncPreviewBusy = false; syncPreviewData = null;
            if (syncInitialDialog?.open) syncInitialDialog.close();
            if (syncPreviewDialog?.open) syncPreviewDialog.close();
            activeTaskId = '';
            activeTaskName = '';
            activeTaskType = 'Traditional';
            canAlterTask = true;
            canDeleteTask = canCreateTask = true;
            saveOutcomeUncertain = false; checkoutConflict = false; manualRecoverySave = false;
            conversionOutcomeUncertain = false;
            dirty = false;
            saving = false;
            logsRequest?.abort(); logsRequest = null; logsGeneration++;
            transferExceptions = [];
            transferExceptionsBusy = false;
            sourceTimestamps = [];
            sourceTimestampsBusy = false;
            sourceTimestampContext = null;
            sourceTimestampEditIndex = -1;
            window.clearTimeout(autoSaveTimer);
            autoSaveTimer = 0;
            activeSavePromise = null;
            autoSaveBlockedSignature = '';
            serverUndoCaptured = false;
            footerSaveError = '';
            taskParameterEditName = '';
            nextActionParameterEditName = '';
            lastUndo = null;
            taskSteps = [];
            schedules = [];
            nextActions = [];
            hostCapabilities = new Map();
            sourceHosts = [];
            destinationHosts = [];
            scriptOptions = [];
            smtpHosts = [];
            taskOptions = [];
            dateListOptions = [];
            tlsCertificates = [];
            sshClientKeys = [];
            notificationTemplates = [];
            taskParameters = [];
            activeStepKey = '';
            activeScheduleKey = '';
            activeNextActionKey = '';
            sourceTabName = 'general';
            hostTabName = 'timeouts';
            processTabName = 'general';
            destinationTabName = 'general';
            destinationHostTabName = 'timeouts';
            temporaryStepSequence = 0;
            temporaryScheduleSequence = 0;
            temporaryScheduleIntervalSequence = 0;
            temporaryScheduleDateListSequence = 0;
            temporaryNextActionSequence = 0;
            stepsBaseline = '';
            schedulesBaseline = '';
            schedulesBaselineHadNativeMulti = false;
            nextActionsBaseline = '';
            taskParametersBaseline = '';
            stepBaselineModels.clear();
            scheduleTabName = 'days';
            scheduleDayMode = 'week';
            nextActionTabName = 'general';
            nextActionHtmlSourceMode = false;
            nextActionHtmlRange = null;
            destinationSmtpHtmlSourceMode = false;
            destinationSmtpHtmlRange = null;
            emailLinkEditorContext = '';
            sourceHostTypeFilters.clear();
            destinationHostTypeFilters.clear();
            processParameterEditName = '';
            folderBrowserTarget = null;
            specialFilterDraft = null;
            closeScheduleTimeMenu();
            if (specialFiltersDialog?.open) specialFiltersDialog.close();
            if (parametersDialog?.open) parametersDialog.close();
            if (addParameterDialog?.open) addParameterDialog.close();
            if (transferExceptionsDialog?.open) transferExceptionsDialog.close();
            if (processParameterDialog?.open) processParameterDialog.close();
            if (nextActionParameterDialog?.open) nextActionParameterDialog.close();
            folderBrowser?.close();
            if (sourceTimestampEditDialog?.open) sourceTimestampEditDialog.close();
            if (sourceTimestampsDialog?.open) sourceTimestampsDialog.close();
            baseline.clear();
            stableValues.clear();
            sourceStableValues.clear();
            processStableValues.clear();
            destinationStableValues.clear();
            nextActionStableValues.clear();
            setChecked('[data-traditional-task-editor-enabled]', false);
            loading.hidden = false;
            error.hidden = true;
            error.textContent = '';
            content.hidden = true;
            metadataFields?.replaceChildren();
            auditHost?.replaceChildren();
            logsRows?.replaceChildren();
            stepItems?.replaceChildren();
            scheduleItems?.replaceChildren();
            nextActionItems?.replaceChildren();
            if (logsLoading) logsLoading.hidden = true;
            if (logsError) { logsError.hidden = true; logsError.textContent = ''; }
            if (logsEmpty) logsEmpty.hidden = true;
            if (logsList) logsList.hidden = true;
            closeActionsMenu();
            closeAddStepMenu();
            closeAddNextActionMenu();
            closeStepActionsMenu();
            closeScheduleActionsMenu();
            closeNextActionActionsMenu();
            if (nextActionLinkDialog?.open) nextActionLinkDialog.close();
            setTab('task', false);
            setWorkspace('steps');
            showNoStep();
            updateUndoState();
            updateDirtyState();
        }

        function populate(data) {
            const task = data.task || {};
            editVersion = String(data.editVersion || '');
            if (!supportsTaskType(task.type)) throw new Error('The MAUI Task Editor supports Traditional, Advanced and Synchronization Tasks.');

            activeTaskId = String(task.id || activeTaskId).trim();
            activeTaskName = String(task.name || activeTaskId).trim();
            activeTaskType = String(task.type || 'Traditional').trim();
            advancedEditor?.clear();
            dialog.dataset.advancedView='flow';
            canAlterTask = task.canAlter !== false;
            canDeleteTask = task.canDelete !== false;
            canCreateTask = task.canCreateTask !== false;
            currentCanRun = task.canRun === true;
            configureTaskKind(task);
            title.textContent = activeTaskName || 'Task';
            description.textContent = String(task.description || '').trim() || 'No description';

            setText('[data-traditional-task-editor-type]', activeTaskType || 'Traditional');
            setText('[data-traditional-task-editor-id]', activeTaskId || '—');
            if (runNowAction) {
                const runnable = String(task.scheduled || '').trim().toLowerCase() !== 'incomplete';
                runNowAction.disabled = !runnable || !currentCanRun;
                runNowAction.title = !runnable ? 'Incomplete Tasks cannot be run.'
                    : !currentCanRun ? 'Your MOVEit account does not permit running this Task.' : '';
            }
            setText('[data-traditional-task-editor-schedule-count]', `(${Number(task.scheduleCount || 0)})`);

            setValue('[data-traditional-task-editor-name]', task.name || '');
            setValue('[data-traditional-task-editor-description-field]', task.description || '');
            setValue('[data-traditional-task-editor-operator-notes]', task.operatorNotes || '');
            setValue('[data-traditional-task-editor-resource-groups]', task.resourceGroups || '—');
            setChecked('[data-traditional-task-editor-enabled]', Boolean(task.enabled));

            hostCapabilities = new Map((Array.isArray(data.hostCapabilities) ? data.hostCapabilities : [])
                .map(normalizeHostCapability)
                .filter(item => item.typeCode)
                .map(item => [item.typeCode.toLowerCase(), item]));
            sourceHosts = Array.isArray(data.sourceHosts) ? data.sourceHosts.map(normalizeHostOption) : [];
            destinationHosts = Array.isArray(data.destinationHosts) ? data.destinationHosts.map(normalizeHostOption) : [];
            scriptOptions = Array.isArray(data.scriptOptions) ? data.scriptOptions.map(normalizeNamedOption) : [];
            smtpHosts = Array.isArray(data.smtpHosts) ? data.smtpHosts.map(normalizeNamedOption) : [];
            taskOptions = Array.isArray(data.taskOptions) ? data.taskOptions.map(normalizeNamedOption) : [];
            dateListOptions = Array.isArray(data.dateListOptions) ? data.dateListOptions.map(normalizeNamedOption) : [];
            tlsCertificates = Array.isArray(data.tlsCertificates) ? data.tlsCertificates.map(normalizeTlsCertificateOption) : [];
            sshClientKeys = Array.isArray(data.sshClientKeys) ? data.sshClientKeys.map(normalizeNamedOption) : [];
            notificationTemplates = Array.isArray(data.notificationTemplates) ? data.notificationTemplates.map(normalizeNotificationTemplate) : [];
            taskParameters = Array.isArray(task.parameters) ? task.parameters.map(normalizeTaskParameter) : [];
            autoSave = Boolean(data.preferences?.autoSave);
            if (autoSaveToggle) autoSaveToggle.checked = autoSave;
            updatePrimarySaveButton();
            renderTaskParameters();
            taskSteps = Array.isArray(task.steps) ? task.steps.map(normalizeStep) : [];
            normalizeStepOrder();
            if (!isAdvancedTask()) scriptGuidance?.prime(taskSteps.filter(step => step.process).map(step => step.process));
            renderStepRail();
            const rawSchedules = Array.isArray(task.schedules) ? task.schedules.map(normalizeSchedule) : [];
            schedules = combineOvernightSchedulePairs(rawSchedules);
            normalizeScheduleIndexes();
            renderScheduleRail();
            nextActions = Array.isArray(task.nextActions) ? task.nextActions.map(normalizeNextAction) : [];
            normalizeNextActionIndexes();
            renderNextActionRail();
            populateNextActionOptions();
            populateNextActionTemplates();
            populateDestinationSmtpTemplates();

            if (isAdvancedTask()) advancedEditor?.load(task.advancedFlow || []);
            populateFolders(data.folders || [], data.folderId);
            populateMetadataFields(data.fields || [], data.metadata || {});
            populateAudit(data.audit || {});
            populateAdvanced(task.advanced || {});

            // Capture the authoritative Step baseline before rendering the initial Step. Actions
            // such as Source Timestamps compare the current Source identity to this baseline.
            captureBaseline();
            if (isAdvancedTask()) { setWorkspace('advanced-step'); if (advancedEditor?.first()) advancedEditor.select(advancedEditor.first()); }
            else if (taskSteps.length > 0) selectStep(taskSteps[0].key);
            else showNoStep();
            loading.hidden = true;
            content.hidden = false;
            tabs.forEach(tab => { tab.disabled = false; });
            // Recompute action eligibility after the loaded content becomes visible.
            // Earlier baseline/selection updates run while content.hidden is still true.
            updateDirtyState();
        }

        function normalizeHostCapability(item) {
            return {
                typeCode: String(item?.typeCode ?? '').trim(),
                type: String(item?.type ?? '').trim(),
                supportsSource: Boolean(item?.supportsSource),
                supportsDestination: Boolean(item?.supportsDestination),
                supportsSourceTimestamps: Boolean(item?.supportsSourceTimestamps),
                sourceWritable: Boolean(item?.sourceWritable),
                destinationWritable: Boolean(item?.destinationWritable),
                sourceBrowsable: Boolean(item?.sourceBrowsable),
                destinationBrowsable: Boolean(item?.destinationBrowsable),
                sourceWriteProfile: String(item?.sourceWriteProfile ?? '').trim(),
                destinationWriteProfile: String(item?.destinationWriteProfile ?? '').trim(),
                sourceBrowseProfile: String(item?.sourceBrowseProfile ?? '').trim(),
                destinationBrowseProfile: String(item?.destinationBrowseProfile ?? '').trim(),
                sourceChangeHostProfile: String(item?.sourceChangeHostProfile ?? '').trim(),
                destinationChangeHostProfile: String(item?.destinationChangeHostProfile ?? '').trim()
            };
        }

        function hostCapability(typeCode) {
            return hostCapabilities.get(String(typeCode || '').trim().toLowerCase()) || null;
        }

        function hostWriteProfile(kind, typeCode) {
            const capability = hostCapability(typeCode);
            if (!capability) return '';
            return kind === 'Source' ? capability.sourceWriteProfile : kind === 'Destination' ? capability.destinationWriteProfile : '';
        }

        function hostBrowseProfile(kind, typeCode) {
            const capability = hostCapability(typeCode);
            if (!capability) return '';
            return kind === 'Source' ? capability.sourceBrowseProfile : kind === 'Destination' ? capability.destinationBrowseProfile : '';
        }

        function isKnownWriteProfile(kind, profile) {
            const normalized = String(profile || '').trim().toLowerCase();
            if (!normalized) return false;
            return kind === 'Source'
                ? SOURCE_WRITE_PROFILES.has(normalized)
                : kind === 'Destination' && DESTINATION_WRITE_PROFILES.has(normalized);
        }

        function canWriteHostStep(kind, typeCode) {
            const capability = hostCapability(typeCode);
            if (!capability) return false;
            const declaredWritable = kind === 'Source'
                ? capability.sourceWritable
                : kind === 'Destination' && capability.destinationWritable;
            return Boolean(declaredWritable) && isKnownWriteProfile(kind, hostWriteProfile(kind, typeCode));
        }

        function canBrowseHostStep(kind, typeCode) {
            const capability = hostCapability(typeCode);
            if (!capability) return false;
            const declaredBrowsable = kind === 'Source'
                ? capability.sourceBrowsable
                : kind === 'Destination' && capability.destinationBrowsable;
            const profile = String(hostBrowseProfile(kind, typeCode) || '').trim().toLowerCase();
            return Boolean(declaredBrowsable) && BROWSE_PROFILES.has(profile);
        }

        function supportsSourceTimestamps(typeCode) {
            return Boolean(hostCapability(typeCode)?.supportsSourceTimestamps);
        }

        function savedSourceForStep(stepKey) {
            if (!stepsBaseline || !stepKey) return null;
            try {
                const savedSteps = JSON.parse(stepsBaseline);
                return Array.isArray(savedSteps)
                    ? (savedSteps.find(item => item?.key === stepKey)?.source || null)
                    : null;
            } catch {
                return null;
            }
        }

        function sourceTimestampSourceNeedsSave(step) {
            if (!step?.source) return true;
            const saved = savedSourceForStep(step.key);
            if (!saved) return true;
            const currentHostId = String(step.source.hostId || '').trim()
                || (String(step.source.hostTypeCode || '').trim().toLowerCase() === 'filesystem' ? '0' : '');
            const savedHostId = String(saved.hostId || '').trim()
                || (String(saved.hostTypeCode || '').trim().toLowerCase() === 'filesystem' ? '0' : '');
            return currentHostId !== savedHostId
                || String(step.source.path || '').trim() !== String(saved.path || '').trim()
                || String(step.source.fileMask || '').trim() !== String(saved.fileMask || '').trim()
                || Boolean(step.source.collectOnlyNewFiles) !== Boolean(saved.collectOnlyNewFiles);
        }

        function hostChangeProfile(kind, typeCode) {
            const capability = hostCapability(typeCode);
            if (!capability) return '';
            return kind === 'Source' ? capability.sourceChangeHostProfile : kind === 'Destination' ? capability.destinationChangeHostProfile : '';
        }

        function canChangeHost(kind, existingTypeCode, requestedTypeCode) {
            if (!canWriteHostStep(kind, requestedTypeCode)) return false;
            if (isSyncTask() && kind === 'Source') return true;
            const existing = String(existingTypeCode || '').trim().toLowerCase();
            const requested = String(requestedTypeCode || '').trim().toLowerCase();
            if (!existing) return true;
            if (existing === requested) return true;
            const existingProfile = hostChangeProfile(kind, existingTypeCode);
            const requestedProfile = hostChangeProfile(kind, requestedTypeCode);
            return Boolean(existingProfile) && existingProfile.toLowerCase() === requestedProfile.toLowerCase();
        }

        function hostTypeLabel(typeCode, fallback = 'Host') {
            return hostCapability(typeCode)?.type || fallback;
        }

        function writableHostTypeCapabilities() {
            return Array.from(hostCapabilities.values())
                .filter(capability => canWriteHostStep('Source', capability.typeCode) || canWriteHostStep('Destination', capability.typeCode))
                .sort((left, right) => left.type.localeCompare(right.type, undefined, { sensitivity: 'base' }));
        }

        function hostTypeFilterMap(kind) {
            return kind === 'Source' ? sourceHostTypeFilters : destinationHostTypeFilters;
        }

        function hostTypeFilterControl(kind) {
            return kind === 'Source' ? sourceHostTypeFilter : destinationHostTypeFilter;
        }

        function hostOptionsForKind(kind) {
            return kind === 'Source' ? sourceHosts : destinationHosts;
        }

        function populateHostTypeFilter(kind, model, stepKey) {
            const select = hostTypeFilterControl(kind);
            if (!select || !model || !stepKey) return '';
            const filters = hostTypeFilterMap(kind);
            let selectedType = String(filters.get(stepKey) || '').trim();
            if (model.hostId && model.hostTypeCode) selectedType = String(model.hostTypeCode).trim();
            select.replaceChildren(new Option('Select a Host Type', ''));
            writableHostTypeCapabilities().forEach(capability => {
                const option = new Option(capability.type, capability.typeCode);
                const supportsDirection = kind === 'Source' ? capability.sourceWritable : capability.destinationWritable;
                const compatible = !model.serverHostTypeCode || canChangeHost(kind, model.serverHostTypeCode, capability.typeCode);
                option.disabled = !supportsDirection || !compatible;
                if (!supportsDirection) option.textContent += kind === 'Source' ? ' — Destination only' : ' — Source only';
                select.appendChild(option);
            });
            if (selectedType && !Array.from(select.options).some(option => option.value.toLowerCase() === selectedType.toLowerCase())) {
                select.appendChild(new Option(hostTypeLabel(selectedType, selectedType), selectedType));
            }
            select.value = selectedType;
            select.disabled = false;
            filters.set(stepKey, selectedType);
            return selectedType;
        }

        function filteredHosts(kind, typeCode) {
            const normalized = String(typeCode || '').trim().toLowerCase();
            if (!normalized) return [];
            return hostOptionsForKind(kind).filter(host => String(host.typeCode || '').trim().toLowerCase() === normalized);
        }

        function normalizeNamedOption(item) {
            return {
                id: String(item?.id ?? ''),
                name: String(item?.name ?? ''),
                type: String(item?.type ?? ''),
                description: String(item?.description ?? '')
            };
        }

        function normalizeTlsCertificateOption(item) {
            return {
                name: String(item?.name ?? ''),
                sha1Thumbprint: String(item?.sha1Thumbprint ?? ''),
                store: String(item?.store ?? 'My'),
                expirationDate: String(item?.expirationDate ?? '')
            };
        }

        function normalizeNotificationTemplate(item) {
            return {
                id: String(item?.id ?? ''),
                name: String(item?.name ?? ''),
                subject: String(item?.subject ?? ''),
                message: String(item?.message ?? '')
            };
        }

        function normalizeNextAction(raw) {
            const type = String(raw?.type || '').trim();
            const index = Number(raw?.index ?? 0);
            const serverIndex = Number(raw?.serverIndex ?? index);
            const id = String(raw?.id ?? '');
            const key = id ? `next-action:${id}` : `server-next-action:${index}`;
            const action = {
                key,
                index,
                serverIndex,
                id,
                type,
                doAfter: String(raw?.doAfter || 'Task'),
                wait: Boolean(raw?.wait),
                doIfFailure: Boolean(raw?.doIfFailure),
                doIfNoAction: Boolean(raw?.doIfNoAction),
                doIfSuccess: Boolean(raw?.doIfSuccess),
                isHtml: Boolean(raw?.isHtml),
                hostId: String(raw?.hostId ?? ''),
                hostName: String(raw?.hostName ?? ''),
                addressTo: String(raw?.addressTo ?? ''),
                subject: String(raw?.subject ?? ''),
                message: String(raw?.message ?? ''),
                taskId: String(raw?.taskId ?? ''),
                taskName: String(raw?.taskName ?? ''),
                parameters: Array.isArray(raw?.parameters) ? raw.parameters.map(normalizeTaskParameter) : []
            };
            seedNextActionStableValues(action);
            return action;
        }

        function normalizeHostOption(host) {
            return {
                id: String(host?.id ?? ''),
                name: String(host?.name ?? ''),
                typeCode: String(host?.typeCode ?? ''),
                type: String(host?.type ?? ''),
                destination: String(host?.destination ?? ''),
                defaults: normalizeHostDefaults(host?.defaults || {})
            };
        }

        function normalizeHostDefaults(defaults = {}) {
            defaults = defaults || {};
            return {
                retryCount: nullableNumber(defaults.retryCount),
                retryTimeoutSeconds: nullableNumber(defaults.retryTimeoutSeconds),
                rescanSeconds: nullableNumber(defaults.rescanSeconds),
                maxFiles: nullableNumber(defaults.maxFiles),
                maxBytes: nullableNumber(defaults.maxBytes),
                useWindowsCopyFileApi: nullableBoolean(defaults.useWindowsCopyFileApi),
                preserveFileTimestamp: nullableBoolean(defaults.preserveFileTimestamp),
                username: String(defaults.username ?? ''),
                clientCertificateSha1: String(defaults.clientCertificateSha1 ?? ''),
                clientCertificateStore: String(defaults.clientCertificateStore ?? ''),
                clientCertificateSubject: String(defaults.clientCertificateSubject ?? ''),
                clientKeyId: String(defaults.clientKeyId ?? ''),
                setFileAttributes: nullableBoolean(defaults.setFileAttributes),
                destinationFileAttributes: String(defaults.destinationFileAttributes ?? ''),
                sort: String(defaults.sort ?? ''),
                connectTimeoutSeconds: nullableNumber(defaults.connectTimeoutSeconds),
                dataTimeoutSeconds: nullableNumber(defaults.dataTimeoutSeconds),
                transferType: String(defaults.transferType || 'binary').toLowerCase(),
                passive: nullableBoolean(defaults.passive),
                useXSha1: nullableBoolean(defaults.useXSha1),
                blindDownload: nullableBoolean(defaults.blindDownload),
                blindUpload: nullableBoolean(defaults.blindUpload),
                resume: nullableBoolean(defaults.resume),
                reuseTlsSession: nullableBoolean(defaults.reuseTlsSession),
                md5Mode: String(defaults.md5Mode || 'Never'),
                md5FileName: String(defaults.md5FileName || 'MD5SUM'),
                bucket: String(defaults.bucket ?? ''),
                container: String(defaults.container ?? ''),
                documentLibrary: String(defaults.documentLibrary ?? ''),
                documentLibraryGuid: String(defaults.documentLibraryGuid ?? ''),
                sharePointAuthenticationMode: nullableNumber(defaults.sharePointAuthenticationMode),
                sharePointClientId: String(defaults.sharePointClientId ?? ''),
                sharePointTenantId: String(defaults.sharePointTenantId ?? ''),
                sharePointRealmId: String(defaults.sharePointRealmId ?? ''),
                googleUseOAuth: nullableBoolean(defaults.googleUseOAuth),
                googleProjectId: String(defaults.googleProjectId ?? ''),
                googleOAuthClientId: String(defaults.googleOAuthClientId ?? ''),
                emailUseOAuth: nullableBoolean(defaults.emailUseOAuth),
                emailOAuthClientId: String(defaults.emailOAuthClientId ?? ''),
                emailOAuthScope: String(defaults.emailOAuthScope ?? ''),
                emailOAuthTokenUrl: String(defaults.emailOAuthTokenUrl ?? ''),
                emailOAuthTenantId: String(defaults.emailOAuthTenantId ?? ''),
                dataIntegrityRule: String(defaults.dataIntegrityRule || 'never').toLowerCase()
            };
        }

        function normalizeTaskParameter(raw) {
            const name = String(raw?.name ?? '').trim();
            return {
                name,
                originalName: String(raw?.originalName ?? name).trim(),
                value: String(raw?.value ?? ''),
                secure: Boolean(raw?.secure),
                valueChanged: Boolean(raw?.valueChanged)
            };
        }

        function isValidParameterName(name) {
            return /^[A-Za-z_][A-Za-z0-9._:]*$/.test(String(name || '').trim());
        }

        function renderParameterRows(parameters, rowsHost, tableHost, emptyHost, options = {}) {
            const items = Array.isArray(parameters) ? parameters : [];
            if (rowsHost) rowsHost.replaceChildren();
            if (tableHost) tableHost.hidden = options.hideTableWhenEmpty === true && items.length === 0;
            if (emptyHost) emptyHost.hidden = items.length !== 0;
            if (!rowsHost || items.length === 0) return;

            const fragment = document.createDocumentFragment();
            items.forEach(parameter => {
                const row = document.createElement('div');
                row.className = 'traditional-task-editor-parameter-row';
                const name = document.createElement('div');
                name.textContent = parameter.name || '—';
                const value = document.createElement('div');
                value.className = parameter.secure ? 'traditional-task-editor-secure-value' : '';
                value.textContent = parameter.secure ? '(suppressed)' : (parameter.value || '');
                const actions = document.createElement('div');
                actions.className = 'traditional-task-editor-parameter-row-actions';

                const edit = document.createElement('button');
                edit.type = 'button';
                edit.className = 'secondary-button compact-button';
                edit.textContent = parameter.secure ? (options.secureEditLabel || 'Change') : (options.editLabel || 'Edit');
                if (options.editDatasetKey) edit.dataset[options.editDatasetKey] = parameter.name || '';
                if (options.secureDatasetKey) edit.dataset[options.secureDatasetKey] = parameter.secure ? 'true' : 'false';

                const remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'secondary-button compact-button danger-text-button';
                remove.textContent = 'Remove';
                if (options.removeDatasetKey) remove.dataset[options.removeDatasetKey] = parameter.name || '';

                actions.append(edit, remove);
                row.append(name, value, actions);
                fragment.appendChild(row);
            });
            rowsHost.appendChild(fragment);
        }

        function renderTaskParameters() {
            if (parametersDialog) {
                const taskNameHost = parametersDialog.querySelector('[data-traditional-task-editor-parameters-task-name]');
                if (taskNameHost) taskNameHost.textContent = activeTaskName || 'Traditional Task';
            }
            renderParameterRows(taskParameters, parameterRows, parameterTable, parametersEmpty, {
                editDatasetKey: 'traditionalTaskEditorParameterEdit',
                secureDatasetKey: 'traditionalTaskEditorParameterSecure',
                removeDatasetKey: 'traditionalTaskEditorParameterRemove'
            });
            scriptGuidance?.appendRequiredRows('task', null, parameterRows, parameterTable, parametersEmpty);
        }

        function normalizeStep(raw) {
            const kind = String(raw?.kind || 'Step');
            const index = Number(raw?.index ?? 0);
            const id = String(raw?.id ?? '');
            const key = id ? `${kind.toLowerCase()}:${id}` : `server-${kind.toLowerCase()}:${index}`;
            const step = {
                key,
                index,
                kind,
                id,
                title: String(raw?.title || kind),
                subtitle: String(raw?.subtitle || ''),
                source: raw?.source ? normalizeSource(raw.source) : null,
                process: raw?.process ? normalizeProcess(raw.process) : null,
                destination: raw?.destination ? normalizeDestination(raw.destination) : null
            };
            if (step.source) seedSourceStableValues(step);
            if (step.process) seedProcessStableValues(step);
            if (step.destination) seedDestinationStableValues(step);
            return step;
        }

        function normalizeSource(raw) {
            return {
                id: String(raw?.id ?? ''),
                hostId: String(raw?.hostId ?? ''),
                hostName: String(raw?.hostName ?? ''),
                hostTypeCode: String(raw?.hostTypeCode ?? ''),
                serverHostTypeCode: String(raw?.serverHostTypeCode ?? raw?.hostTypeCode ?? ''),
                hostType: String(raw?.hostType ?? ''),
                path: String(raw?.path ?? ''),
                useDefaultBucket: raw?.useDefaultBucket == null ? true : Boolean(raw.useDefaultBucket),
                bucket: String(raw?.bucket ?? ''),
                useDefaultContainer: raw?.useDefaultContainer == null ? true : Boolean(raw.useDefaultContainer),
                container: String(raw?.container ?? ''),
                useDefaultDocumentLibrary: raw?.useDefaultDocumentLibrary == null ? true : Boolean(raw.useDefaultDocumentLibrary),
                documentLibrary: String(raw?.documentLibrary ?? ''),
                documentLibraryGuid: String(raw?.documentLibraryGuid ?? ''),
                requiresFolderSelection: Boolean(raw?.requiresFolderSelection),
                fileMask: String(raw?.fileMask ?? ''),
                excludeFolders: String(raw?.excludeFolders ?? ''),
                ignoreFolders: Boolean(String(raw?.excludeFolders ?? '').trim()),
                searchSubdirectories: Boolean(raw?.searchSubdirectories),
                excludeFiles: String(raw?.excludeFiles ?? ''),
                ignoreFiles: Boolean(String(raw?.excludeFiles ?? '').trim()),
                hasSpecialFilters: Boolean(raw?.hasSpecialFilters),
                specialFilters: normalizeSpecialFilters(raw?.specialFilters),
                collectOnlyNewFiles: Boolean(raw?.collectOnlyNewFiles),
                expandZipFiles: Boolean(raw?.expandZipFiles),
                retryIfNoFilesFound: Boolean(raw?.retryIfNoFilesFound),
                deleteOriginalAction: Number(raw?.deleteOriginalAction ?? 0),
                renameTo: String(raw?.renameTo ?? ''),
                renamedFilesMayOverwrite: Boolean(raw?.renamedFilesMayOverwrite),
                overridePreserveFileTimestamp: raw?.useDefaultPreserveFileTimestamp == null ? false : !Boolean(raw.useDefaultPreserveFileTimestamp),
                preserveFileTimestamp: Boolean(raw?.preserveFileTimestamp),
                overrideRetryCount: !Boolean(raw?.useDefaultRetryCount),
                retryCount: nullableNumber(raw?.retryCount),
                overrideRetryTimeout: !Boolean(raw?.useDefaultRetryTimeout),
                retryTimeoutSeconds: nullableNumber(raw?.retryTimeoutSeconds),
                overrideRescanTime: !Boolean(raw?.useDefaultRescanTime),
                rescanSeconds: nullableNumber(raw?.rescanSeconds),
                overrideMaxFiles: !Boolean(raw?.useDefaultMaxFiles),
                maxFiles: nullableNumber(raw?.maxFiles),
                overrideMaxBytes: !Boolean(raw?.useDefaultMaxBytes),
                maxBytes: nullableNumber(raw?.maxBytes),
                overrideWindowsCopyFileApi: !Boolean(raw?.useDefaultWindowsCopyFileApi),
                useWindowsCopyFileApi: nullableBoolean(raw?.useWindowsCopyFileApi),
                overrideCredentials: raw?.useDefaultCredentials == null ? false : !Boolean(raw.useDefaultCredentials),
                username: String(raw?.username ?? ''),
                passwordConfigured: Boolean(raw?.passwordConfigured),
                password: '',
                overrideClientCertificate: raw?.useDefaultClientCertificate == null ? false : !Boolean(raw.useDefaultClientCertificate),
                clientCertificateSha1: String(raw?.clientCertificateSha1 ?? ''),
                clientCertificateStore: String(raw?.clientCertificateStore ?? ''),
                clientCertificateSubject: String(raw?.clientCertificateSubject ?? ''),
                sharePointCertificateSha1: String(raw?.clientCertificateSha1 ?? ''),
                overrideClientKey: raw?.useDefaultClientKey == null ? false : !Boolean(raw.useDefaultClientKey),
                clientKeyId: String(raw?.clientKeyId ?? ''),
                sharePointAuthenticationMode: raw?.sharePointAuthenticationMode == null ? null : Number(raw.sharePointAuthenticationMode),
                sharePointClientId: String(raw?.sharePointClientId ?? ''),
                sharePointTenantId: String(raw?.sharePointTenantId ?? ''),
                sharePointRealmId: String(raw?.sharePointRealmId ?? ''),
                sharePointClientSecretConfigured: Boolean(raw?.sharePointClientSecretConfigured),
                sharePointClientSecret: '',
                googleUseOAuth: raw?.googleUseOAuth == null ? null : Boolean(raw.googleUseOAuth),
                googleProjectId: String(raw?.googleProjectId ?? ''),
                googleOAuthClientId: String(raw?.googleOAuthClientId ?? ''),
                googleOAuthClientSecretConfigured: Boolean(raw?.googleOAuthClientSecretConfigured),
                googleOAuthClientSecret: '',
                emailUseOAuth: raw?.emailUseOAuth == null ? null : Boolean(raw.emailUseOAuth),
                emailOAuthType: String(raw?.emailOAuthTenantId ?? '').trim() ? 'microsoft365' : 'custom',
                emailOAuthUsername: String(raw?.username ?? ''),
                emailOAuthClientId: String(raw?.emailOAuthClientId ?? ''),
                emailOAuthScope: String(raw?.emailOAuthScope ?? ''),
                emailOAuthTokenUrl: String(raw?.emailOAuthTokenUrl ?? ''),
                emailOAuthTenantId: String(raw?.emailOAuthTenantId ?? ''),
                emailOAuthClientSecretConfigured: Boolean(raw?.emailOAuthClientSecretConfigured),
                emailOAuthClientSecret: '',
                overrideFileSorting: raw?.useDefaultFileSorting == null ? false : !Boolean(raw.useDefaultFileSorting),
                sort: String(raw?.sort ?? ''),
                overrideConnectTimeout: raw?.useDefaultConnectTimeout == null ? false : !Boolean(raw.useDefaultConnectTimeout),
                connectTimeoutSeconds: nullableNumber(raw?.connectTimeoutSeconds),
                overrideDataTimeout: raw?.useDefaultDataTimeout == null ? false : !Boolean(raw.useDefaultDataTimeout),
                dataTimeoutSeconds: nullableNumber(raw?.dataTimeoutSeconds),
                additionalCommandsBefore: String(raw?.additionalCommandsBefore ?? ''),
                additionalCommandsAfter: String(raw?.additionalCommandsAfter ?? ''),
                overrideTransferType: raw?.useDefaultTransferType == null ? false : !Boolean(raw.useDefaultTransferType),
                transferType: String(raw?.transferType || 'binary').toLowerCase(),
                overridePassive: raw?.useDefaultPassive == null ? false : !Boolean(raw.useDefaultPassive),
                passive: nullableBoolean(raw?.passive),
                overrideXSha1: raw?.useDefaultXSha1 == null ? false : !Boolean(raw.useDefaultXSha1),
                useXSha1: nullableBoolean(raw?.useXSha1),
                overrideBlindDownload: raw?.useDefaultBlindDownload == null ? false : !Boolean(raw.useDefaultBlindDownload),
                blindDownload: nullableBoolean(raw?.blindDownload),
                overrideResume: raw?.useDefaultResume == null ? false : !Boolean(raw.useDefaultResume),
                resume: nullableBoolean(raw?.resume),
                overrideReuseTlsSession: raw?.useDefaultReuseTlsSession == null ? false : !Boolean(raw.useDefaultReuseTlsSession),
                reuseTlsSession: nullableBoolean(raw?.reuseTlsSession),
                overrideMd5Mode: raw?.useDefaultMd5 == null ? false : !Boolean(raw.useDefaultMd5),
                md5Mode: String(raw?.md5Mode || 'Never'),
                overrideMd5FileName: raw?.useDefaultMd5FileName == null ? false : !Boolean(raw.useDefaultMd5FileName),
                md5FileName: String(raw?.md5FileName || 'MD5SUM'),
                overrideDataIntegrity: raw?.useDefaultDataIntegrity == null ? false : !Boolean(raw.useDefaultDataIntegrity),
                dataIntegrityRule: String(raw?.dataIntegrityRule || 'never').toLowerCase(),
                account: '',
                accountConfigured: Boolean(raw?.accountConfigured),
                hostDefaults: normalizeHostDefaults(raw?.hostDefaults || {})
            };
        }

        function normalizeSpecialFilters(raw) {
            if (!raw) return null;
            return {
                matchMode: String(raw?.matchMode || 'all').toLowerCase() === 'any' ? 'any' : 'all',
                hasUnsupportedCriteria: Boolean(raw?.hasUnsupportedCriteria),
                filters: Array.isArray(raw?.filters) ? raw.filters.map(filter => ({
                    type: String(filter?.type || 'fileSize'),
                    operator: String(filter?.operator || 'eq'),
                    value: String(filter?.value ?? ''),
                    unit: String(filter?.unit || (String(filter?.type || '') === 'fileModified' ? 'hours' : 'B'))
                })) : []
            };
        }

        function normalizeProcess(raw) {
            return {
                id: String(raw?.id ?? ''),
                scriptId: String(raw?.scriptId ?? ''),
                scriptName: String(raw?.scriptName ?? ''),
                scriptType: String(raw?.scriptType ?? ''),
                run: String(raw?.run || 'PerFile'),
                isDestination: Boolean(raw?.isDestination),
                parameters: Array.isArray(raw?.parameters) ? raw.parameters.map(normalizeTaskParameter) : []
            };
        }

        function normalizeDestination(raw) {
            return {
                id: String(raw?.id ?? ''),
                hostId: String(raw?.hostId ?? ''),
                hostName: String(raw?.hostName ?? ''),
                hostTypeCode: String(raw?.hostTypeCode ?? ''),
                serverHostTypeCode: String(raw?.serverHostTypeCode ?? raw?.hostTypeCode ?? ''),
                hostType: String(raw?.hostType ?? ''),
                path: String(raw?.path ?? ''),
                useDefaultBucket: raw?.useDefaultBucket == null ? true : Boolean(raw.useDefaultBucket),
                bucket: String(raw?.bucket ?? ''),
                useDefaultContainer: raw?.useDefaultContainer == null ? true : Boolean(raw.useDefaultContainer),
                container: String(raw?.container ?? ''),
                useDefaultDocumentLibrary: raw?.useDefaultDocumentLibrary == null ? true : Boolean(raw.useDefaultDocumentLibrary),
                documentLibrary: String(raw?.documentLibrary ?? ''),
                documentLibraryGuid: String(raw?.documentLibraryGuid ?? ''),
                addressTo: String(raw?.addressTo ?? ''),
                subject: String(raw?.subject ?? ''),
                message: String(raw?.message ?? ''),
                isHtml: Boolean(raw?.isHtml),
                requiresFolderSelection: Boolean(raw?.requiresFolderSelection),
                useRelativeSubdirectories: Boolean(raw?.useRelativeSubdirectories),
                forceDirectory: Boolean(raw?.forceDirectory),
                zip: Boolean(raw?.zip),
                useOriginalName: raw?.useOriginalName == null ? true : Boolean(raw.useOriginalName),
                fileName: String(raw?.fileName ?? ''),
                overwriteAction: Number(raw?.overwriteAction ?? 0),
                overrideRetryCount: !Boolean(raw?.useDefaultRetryCount),
                retryCount: nullableNumber(raw?.retryCount),
                overrideRetryTimeout: !Boolean(raw?.useDefaultRetryTimeout),
                retryTimeoutSeconds: nullableNumber(raw?.retryTimeoutSeconds),
                overrideWindowsCopyFileApi: !Boolean(raw?.useDefaultWindowsCopyFileApi),
                useWindowsCopyFileApi: nullableBoolean(raw?.useWindowsCopyFileApi),
                overridePreserveFileTimestamp: !Boolean(raw?.useDefaultPreserveFileTimestamp),
                preserveFileTimestamp: nullableBoolean(raw?.preserveFileTimestamp),
                overrideCredentials: raw?.useDefaultCredentials == null ? false : !Boolean(raw.useDefaultCredentials),
                username: String(raw?.username ?? ''),
                passwordConfigured: Boolean(raw?.passwordConfigured),
                password: '',
                overrideClientCertificate: raw?.useDefaultClientCertificate == null ? false : !Boolean(raw.useDefaultClientCertificate),
                clientCertificateSha1: String(raw?.clientCertificateSha1 ?? ''),
                clientCertificateStore: String(raw?.clientCertificateStore ?? ''),
                clientCertificateSubject: String(raw?.clientCertificateSubject ?? ''),
                sharePointCertificateSha1: String(raw?.clientCertificateSha1 ?? ''),
                overrideClientKey: raw?.useDefaultClientKey == null ? false : !Boolean(raw.useDefaultClientKey),
                clientKeyId: String(raw?.clientKeyId ?? ''),
                sharePointAuthenticationMode: raw?.sharePointAuthenticationMode == null ? null : Number(raw.sharePointAuthenticationMode),
                sharePointClientId: String(raw?.sharePointClientId ?? ''),
                sharePointTenantId: String(raw?.sharePointTenantId ?? ''),
                sharePointRealmId: String(raw?.sharePointRealmId ?? ''),
                sharePointClientSecretConfigured: Boolean(raw?.sharePointClientSecretConfigured),
                sharePointClientSecret: '',
                googleUseOAuth: raw?.googleUseOAuth == null ? null : Boolean(raw.googleUseOAuth),
                googleProjectId: String(raw?.googleProjectId ?? ''),
                googleOAuthClientId: String(raw?.googleOAuthClientId ?? ''),
                googleOAuthClientSecretConfigured: Boolean(raw?.googleOAuthClientSecretConfigured),
                googleOAuthClientSecret: '',
                emailUseOAuth: raw?.emailUseOAuth == null ? null : Boolean(raw.emailUseOAuth),
                emailOAuthType: String(raw?.emailOAuthTenantId ?? '').trim() ? 'microsoft365' : 'custom',
                emailOAuthUsername: String(raw?.username ?? ''),
                emailOAuthClientId: String(raw?.emailOAuthClientId ?? ''),
                emailOAuthScope: String(raw?.emailOAuthScope ?? ''),
                emailOAuthTokenUrl: String(raw?.emailOAuthTokenUrl ?? ''),
                emailOAuthTenantId: String(raw?.emailOAuthTenantId ?? ''),
                emailOAuthClientSecretConfigured: Boolean(raw?.emailOAuthClientSecretConfigured),
                emailOAuthClientSecret: '',
                overrideConnectTimeout: raw?.useDefaultConnectTimeout == null ? false : !Boolean(raw.useDefaultConnectTimeout),
                connectTimeoutSeconds: nullableNumber(raw?.connectTimeoutSeconds),
                overrideDataTimeout: raw?.useDefaultDataTimeout == null ? false : !Boolean(raw.useDefaultDataTimeout),
                dataTimeoutSeconds: nullableNumber(raw?.dataTimeoutSeconds),
                additionalCommandsBefore: String(raw?.additionalCommandsBefore ?? ''),
                additionalCommandsAfter: String(raw?.additionalCommandsAfter ?? ''),
                overrideTransferType: raw?.useDefaultTransferType == null ? false : !Boolean(raw.useDefaultTransferType),
                transferType: String(raw?.transferType || 'binary').toLowerCase(),
                overridePassive: raw?.useDefaultPassive == null ? false : !Boolean(raw.useDefaultPassive),
                passive: nullableBoolean(raw?.passive),
                overrideXSha1: raw?.useDefaultXSha1 == null ? false : !Boolean(raw.useDefaultXSha1),
                useXSha1: nullableBoolean(raw?.useXSha1),
                overrideBlindUpload: raw?.useDefaultBlindUpload == null ? false : !Boolean(raw.useDefaultBlindUpload),
                blindUpload: nullableBoolean(raw?.blindUpload),
                overrideResume: raw?.useDefaultResume == null ? false : !Boolean(raw.useDefaultResume),
                resume: nullableBoolean(raw?.resume),
                overrideReuseTlsSession: raw?.useDefaultReuseTlsSession == null ? false : !Boolean(raw.useDefaultReuseTlsSession),
                reuseTlsSession: nullableBoolean(raw?.reuseTlsSession),
                overrideFileAttributes: raw?.useDefaultFileAttributes == null ? false : !Boolean(raw.useDefaultFileAttributes),
                setFileAttributes: nullableBoolean(raw?.setFileAttributes),
                destinationFileAttributes: String(raw?.destinationFileAttributes ?? ''),
                account: '',
                accountConfigured: Boolean(raw?.accountConfigured),
                hostDefaults: normalizeHostDefaults(raw?.hostDefaults || {})
            };
        }

        function normalizeSchedule(raw) {
            const index = Number(raw?.index ?? 0);
            const serverIndex = Number(raw?.serverIndex ?? index);
            const id = String(raw?.id ?? '');
            const key = id ? `schedule:${index}:${id}` : `server-schedule:${index}`;
            const schedule = {
                key,
                index,
                serverIndex,
                id,
                multi: Number(raw?.multi ?? 0),
                daysOfWeek: Array.isArray(raw?.daysOfWeek) ? raw.daysOfWeek.map(canonicalScheduleWeekday).filter(Boolean) : [],
                daysOfMonth: Array.isArray(raw?.daysOfMonth) ? raw.daysOfMonth.map(value => Number(value)).filter(value => Number.isInteger(value) && value >= 1 && value <= 31) : [],
                dateLists: Array.isArray(raw?.dateLists) ? raw.dateLists.map((reference, referenceIndex) => ({
                    key: String(reference?.id || '') ? `schedule-date-list:${index}:${referenceIndex}:${String(reference.id)}` : `server-schedule-date-list:${index}:${referenceIndex}`,
                    id: String(reference?.id ?? ''),
                    name: String(reference?.name ?? ''),
                    run: Boolean(reference?.run)
                })) : [],
                intervals: Array.isArray(raw?.intervals) ? raw.intervals.map((interval, intervalIndex) => {
                    const startTime = normalizeScheduleTime(interval?.startTime);
                    const endTime = normalizeScheduleTime(interval?.endTime || interval?.startTime);
                    const everyMinutes = Math.max(0, Number(interval?.everyMinutes ?? 0) || 0);
                    return {
                        key: String(interval?.id || '') ? `schedule-interval:${index}:${intervalIndex}:${String(interval.id)}` : `server-schedule-interval:${index}:${intervalIndex}`,
                        serverIndex: Number(interval?.serverIndex ?? intervalIndex),
                        id: String(interval?.id ?? ''),
                        startTime,
                        endTime,
                        everyMinutes,
                        repeat: everyMinutes > 0
                    };
                }) : [],
                onlyUntilFirstSuccess: Boolean(raw?.onlyUntilFirstSuccess),
                failIfNoSuccessInSchedule: Boolean(raw?.failIfNoSuccessInSchedule),
                runEvenIfNotifications: Boolean(raw?.runEvenIfNotifications)
            };
            schedule.uiDayMode = inferScheduleDayMode(schedule);
            schedule.serverWasOvernightUnsplit = schedule.multi === 0 && scheduleHasCrossingInterval(schedule);
            return schedule;
        }

        function combineOvernightSchedulePairs(items) {
            const combined = [];
            for (let index = 0; index < items.length; index++) {
                const before = items[index];
                const after = items[index + 1];
                if (canCombineOvernightSchedulePair(before, after)) {
                    combined.push(combineOvernightSchedulePair(before, after));
                    index++;
                    continue;
                }
                combined.push(before);
            }
            return combined;
        }

        function shiftScheduleWeekdaysForward(days) {
            const order = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
            return (Array.isArray(days) ? days : []).map(value => {
                const canonical = canonicalScheduleWeekday(value);
                const index = order.findIndex(day => day.toLowerCase() === String(canonical || '').toLowerCase());
                return index >= 0 ? order[(index + 1) % order.length] : canonical;
            }).filter(Boolean);
        }

        function shiftScheduleMonthDaysForward(days) {
            return (Array.isArray(days) ? days : []).map(value => {
                const day = Number(value);
                if (!Number.isInteger(day) || day < 1 || day > 31) return null;
                return day === 31 ? 1 : day + 1;
            }).filter(value => value != null);
        }

        function canCombineOvernightSchedulePair(before, after) {
            if (!before || !after || Number(before.multi) !== 1 || Number(after.multi) !== 2) return false;
            if ((before.dateLists || []).length || (after.dateLists || []).length) return false;

            // Native MOVEit stores the post-midnight segment on the following selected day.
            // Captured Web Admin exports establish this independently for weekdays and DayOfMonth.
            if (!scheduleArraysEqual(shiftScheduleWeekdaysForward(before.daysOfWeek), after.daysOfWeek, value => String(value || '').toLowerCase())) return false;
            if (!scheduleArraysEqual(shiftScheduleMonthDaysForward(before.daysOfMonth), after.daysOfMonth, value => Number(value))) return false;
            if (Boolean(before.onlyUntilFirstSuccess) !== Boolean(after.onlyUntilFirstSuccess)
                || Boolean(before.failIfNoSuccessInSchedule) !== Boolean(after.failIfNoSuccessInSchedule)
                || Boolean(before.runEvenIfNotifications) !== Boolean(after.runEvenIfNotifications)) return false;

            const beforeId = String(before.id || '').trim();
            const afterId = String(after.id || '').trim();
            if (beforeId && afterId && beforeId.toLowerCase() !== afterId.toLowerCase()) return false;

            const beforeIntervals = before.intervals || [];
            const afterIntervals = after.intervals || [];
            if (!beforeIntervals.length || beforeIntervals.length !== afterIntervals.length) return false;
            for (let index = 0; index < beforeIntervals.length; index++) {
                const pre = beforeIntervals[index];
                const post = afterIntervals[index];
                const start = scheduleTimeMinutes(pre.startTime);
                const preEnd = scheduleTimeMinutes(pre.endTime);
                const postStart = scheduleTimeMinutes(post.startTime);
                const end = scheduleTimeMinutes(post.endTime);
                if ([start, preEnd, postStart, end].some(value => value == null)) return false;
                const every = Number(pre.everyMinutes || 0);
                if (!pre.repeat || !post.repeat || every <= 0 || every !== Number(post.everyMinutes)) return false;
                const expectedPostStart = overnightPostStartMinutes(start, every);
                if (preEnd !== 1439 || expectedPostStart == null || postStart !== expectedPostStart || start <= end) return false;
                const preId = String(pre.id || '').trim();
                const postId = String(post.id || '').trim();
                if (preId && postId && preId.toLowerCase() !== postId.toLowerCase()) return false;
            }
            return true;
        }

        function combineOvernightSchedulePair(before, after) {
            const schedule = deepClone(before);
            schedule.key = `overnight:${before.key}|${after.key}`;
            schedule.overnightPair = true;
            schedule.pairedServerIndex = Number(after.serverIndex ?? -1);
            schedule.pairedId = String(after.id ?? '');
            schedule.serverWasOvernightUnsplit = false;
            schedule.intervals = (before.intervals || []).map((pre, index) => {
                const post = after.intervals[index];
                return {
                    ...deepClone(pre),
                    key: `overnight-interval:${pre.key}|${post.key}`,
                    endTime: post.endTime,
                    pairedServerIndex: Number(post.serverIndex ?? index),
                    pairedId: String(post.id ?? ''),
                    splitPreEndTime: pre.endTime,
                    splitPostStartTime: post.startTime,
                    originalStartTime: pre.startTime,
                    originalEveryMinutes: Number(pre.everyMinutes || 0)
                };
            });
            return schedule;
        }

        function scheduleArraysEqual(left, right, normalize) {
            const a = Array.isArray(left) ? left.map(normalize) : [];
            const b = Array.isArray(right) ? right.map(normalize) : [];
            return a.length === b.length && a.every((value, index) => value === b[index]);
        }

        function scheduleTimeMinutes(value) {
            const normalized = normalizeScheduleTime(value);
            const match = /^(\d{2}):(\d{2})$/.exec(normalized);
            if (!match) return null;
            return Number(match[1]) * 60 + Number(match[2]);
        }

        function scheduleMinutesTime(value) {
            const minutes = Number(value);
            if (!Number.isInteger(minutes) || minutes < 0 || minutes >= 1440) return '';
            return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
        }

        // MOVEit's native overnight pair continues the repeat cadence across midnight. For example,
        // 20:35 every 15 minutes becomes Multi=1 20:35-23:59 and Multi=2 00:05-End.
        // If the cadence lands exactly on midnight the post-midnight segment starts at 00:00.
        function overnightPostStartMinutes(startMinutes, everyMinutes) {
            const start = Number(startMinutes);
            const every = Number(everyMinutes);
            if (!Number.isInteger(start) || start < 0 || start >= 1440 || !Number.isInteger(every) || every <= 0) return null;
            const stepsToMidnight = Math.ceil((1440 - start) / every);
            return (start + (stepsToMidnight * every)) % 1440;
        }

        function overnightPostStartTime(startTime, everyMinutes) {
            const start = scheduleTimeMinutes(startTime);
            if (start == null) return '';
            const postStart = overnightPostStartMinutes(start, everyMinutes);
            return postStart == null ? '' : scheduleMinutesTime(postStart);
        }

        function scheduleHasCrossingInterval(schedule) {
            return (schedule?.intervals || []).some(interval => {
                if (!interval.repeat) return false;
                const start = scheduleTimeMinutes(interval.startTime);
                const end = scheduleTimeMinutes(interval.endTime);
                return start != null && end != null && start > end;
            });
        }

        function canonicalScheduleWeekday(value) {
            const text = String(value || '').trim();
            if (!text) return '';
            const days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
            const exact = days.find(day => day.toLowerCase() === text.toLowerCase());
            if (exact) return exact;
            if (text.length >= 3) {
                const prefix = days.find(day => day.toLowerCase().startsWith(text.toLowerCase()) || text.toLowerCase().startsWith(day.slice(0, 3).toLowerCase()));
                if (prefix) return prefix;
            }
            return text;
        }

        function normalizeScheduleTime(value) {
            const text = String(value ?? '').trim();
            if (!text) return '';

            const twelveHour = /^(\d{1,2}):(\d{2})\s*([AP])M$/i.exec(text);
            if (twelveHour) {
                const displayHour = Number(twelveHour[1]);
                const minute = Number(twelveHour[2]);
                if (displayHour < 1 || displayHour > 12 || minute < 0 || minute > 59) return text;
                let hour = displayHour % 12;
                if (twelveHour[3].toUpperCase() === 'P') hour += 12;
                return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
            }

            const twentyFourHour = /^(\d{1,2}):(\d{2})$/.exec(text);
            if (!twentyFourHour) return text;
            const hour = Number(twentyFourHour[1]);
            const minute = Number(twentyFourHour[2]);
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return text;
            return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
        }

        function normalizeScheduleIndexes() {
            schedules.forEach((schedule, index) => { schedule.index = index; });
        }

        function renderScheduleRail() {
            if (!scheduleItems) return;
            scheduleItems.replaceChildren();
            normalizeScheduleIndexes();
            setText('[data-traditional-task-editor-schedule-count]', `(${schedules.length})`);
            if (schedules.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'traditional-task-editor-empty-item';
                empty.textContent = 'No schedules are currently defined.';
                scheduleItems.appendChild(empty);
                return;
            }

            const fragment = document.createDocumentFragment();
            schedules.forEach((schedule, index) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'traditional-task-editor-step-item traditional-task-editor-schedule-item';
                button.classList.toggle('active', schedule.key === activeScheduleKey);
                button.dataset.traditionalTaskEditorScheduleKey = schedule.key;

                const number = document.createElement('span');
                number.className = 'traditional-task-editor-step-number';
                number.textContent = String(index + 1);

                const copy = document.createElement('span');
                copy.className = 'traditional-task-editor-step-copy';
                const name = document.createElement('strong');
                name.textContent = `Schedule ${index + 1}`;
                const detail = document.createElement('small');
                detail.textContent = scheduleRailDetail(schedule);
                copy.append(name, detail);
                button.append(number, copy);
                fragment.appendChild(button);
            });
            scheduleItems.appendChild(fragment);
        }

        function activeSchedule() {
            return schedules.find(item => item.key === activeScheduleKey) || null;
        }

        function selectSchedule(scheduleKey) {
            const schedule = schedules.find(item => item.key === scheduleKey);
            activeScheduleKey = schedule?.key || '';
            renderScheduleRail();
            if (!schedule) { showNoSchedule(); return; }

            if (scheduleEmpty) scheduleEmpty.hidden = true;
            if (scheduleDetail) scheduleDetail.hidden = false;
            if (scheduleKicker) scheduleKicker.textContent = `Schedule ${schedule.index + 1}`;
            if (scheduleTitle) scheduleTitle.textContent = scheduleScheduleTitle(schedule);
            renderSchedule(schedule);
        }

        function showNoSchedule() {
            activeScheduleKey = '';
            renderScheduleRail();
            updateScheduleSummaries(null);
            if (scheduleEmpty) scheduleEmpty.hidden = false;
            if (scheduleDetail) scheduleDetail.hidden = true;
        }

        function scheduleScheduleTitle(schedule) {
            if (scheduleHasCrossingInterval(schedule)) return 'Overnight Schedule';
            if (schedule?.multi === 1 && !schedule?.overnightPair) return 'Schedule (before midnight)';
            if (schedule?.multi === 2 && !schedule?.overnightPair) return 'Schedule (after midnight)';
            return 'Schedule';
        }

        function scheduleRailDetail(schedule) {
            const dayText = scheduleDaySummary(schedule, true);
            const timeText = scheduleTimeSummary(schedule, true);
            return [dayText, timeText].filter(Boolean).join(' · ') || 'Schedule';
        }

        function inferScheduleDayMode(schedule) {
            if ((schedule?.dateLists || []).some(reference => String(reference?.id || '').trim())) return 'custom';
            if ((schedule?.daysOfMonth || []).length && !(schedule?.daysOfWeek || []).length) return 'month';
            return 'week';
        }

        function scheduleDaySummary(schedule, compact = false) {
            const labels = [];
            const weekdayOrder = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
            const dayMap = { Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed', Thursday: 'Thu', Friday: 'Fri', Saturday: 'Sat', Sunday: 'Sun' };
            const selectedWeekdays = weekdayOrder.filter(day => (schedule?.daysOfWeek || []).some(value => String(value).toLowerCase() === day.toLowerCase()));
            if (selectedWeekdays.length) labels.push(selectedWeekdays.map(day => dayMap[day]).join(', '));

            const monthDays = [...(schedule?.daysOfMonth || [])].sort((a, b) => a - b);
            if (monthDays.length) labels.push(compact ? `Month: ${monthDays.join(', ')}` : `days ${monthDays.join(', ')} of each month`);

            const dateRefs = (schedule?.dateLists || []).filter(reference => reference.id);
            if (dateRefs.length) {
                const dateText = dateRefs.map(reference => `${reference.run ? 'Include' : 'Exclude'} ${dateListName(reference.id) || reference.name || reference.id}`).join(', ');
                labels.push(dateText);
            }

            if (!labels.length) return compact ? 'Days needed' : '(Days needed)';
            return labels.join(compact ? ' + ' : '; ');
        }

        function scheduleTimeSummary(schedule, compact = false) {
            const intervals = schedule?.intervals || [];
            if (!intervals.length) return compact ? 'No time' : 'no scheduled time is defined';
            const values = intervals.map(interval => {
                const start = formatScheduleTime(interval.startTime);
                if (!interval.repeat) return compact ? (start || 'Time not set') : `once at ${start || 'a time that is not set'}`;
                const minutes = Number(interval.everyMinutes || 0);
                const end = formatScheduleTime(interval.endTime);
                const cadence = minutes > 0 ? `every ${minutes} min` : 'with repeat interval not set';
                return compact
                    ? `${start || 'Time not set'} ${cadence}${end ? ` until ${end}` : ''}`
                    : `starting at ${start || 'a time that is not set'}, ${cadence}${end ? ` until ${end}` : ''}`;
            });
            return values.join(compact ? ', ' : '; and ');
        }

        function scheduleSummaryText(schedule) {
            if (!schedule) return 'Task will run: —';
            const days = scheduleDaySummary(schedule, false);
            const times = scheduleTimeSummary(schedule, false);
            const suffix = scheduleHasCrossingInterval(schedule)
                ? ' This schedule spans midnight.'
                : ((schedule?.multi === 1 || schedule?.multi === 2) && !schedule?.overnightPair ? ' MOVEit returned this as an unmatched overnight segment; MAUI will preserve it read-only.' : '');
            return `Task will run: ${days} ${times}.${suffix}`;
        }

        function updateScheduleSummaries(schedule) {
            const text = scheduleSummaryText(schedule);
            if (scheduleHeadingSummary) scheduleHeadingSummary.textContent = text;
            if (scheduleSummary) scheduleSummary.textContent = text;
        }

        function formatScheduleTime(value) {
            const text = normalizeScheduleTime(value);
            const match = /^(\d{2}):(\d{2})$/.exec(text);
            if (!match) return text;
            const hour = Number(match[1]);
            const minute = Number(match[2]);
            const suffix = hour >= 12 ? 'PM' : 'AM';
            const displayHour = hour % 12 || 12;
            return `${displayHour}:${String(minute).padStart(2, '0')} ${suffix}`;
        }

        function isScheduleReadOnly(schedule) {
            return Number(schedule?.multi || 0) !== 0 && !schedule?.overnightPair;
        }

        function isPairedOvernightSchedule(schedule) {
            return Boolean(schedule?.overnightPair);
        }

        function renderSchedule(schedule) {
            if (!schedule) return;
            const readOnly = isScheduleReadOnly(schedule);
            if (scheduleReadOnlyNotice) scheduleReadOnlyNotice.hidden = !readOnly;
            scheduleActionButtons.forEach(button => {
                button.disabled = readOnly;
                button.title = '';
            });
            const logicalOvernight = scheduleHasCrossingInterval(schedule);
            if (scheduleAddDateListButton) {
                scheduleAddDateListButton.disabled = readOnly || logicalOvernight;
                scheduleAddDateListButton.title = logicalOvernight ? 'MOVEit Date List schedules must remain within the selected date and cannot span midnight.' : '';
            }
            if (scheduleAddTimeButton) {
                scheduleAddTimeButton.disabled = readOnly || logicalOvernight;
                scheduleAddTimeButton.title = logicalOvernight ? 'MOVEit stores an overnight time as a linked Multi=1/Multi=2 pair. Change this Schedule back to a same-day time before adding another time row.' : '';
            }
            const customDayButton = dialog.querySelector('[data-traditional-task-editor-schedule-day-mode="custom"]');
            if (customDayButton) {
                customDayButton.disabled = readOnly || logicalOvernight;
                customDayButton.title = logicalOvernight ? 'MOVEit Date List schedules must remain within the selected date and cannot span midnight.' : '';
            }
            let dayMode = ['week', 'month', 'custom'].includes(schedule.uiDayMode) ? schedule.uiDayMode : inferScheduleDayMode(schedule);
            if (logicalOvernight && dayMode === 'custom') dayMode = 'week';
            renderScheduleDays(schedule);
            renderScheduleDateLists(schedule);
            renderScheduleIntervals(schedule);
            setScheduleControlValue('onlyUntilFirstSuccess', schedule.onlyUntilFirstSuccess);
            setScheduleControlValue('failIfNoSuccessInSchedule', schedule.failIfNoSuccessInSchedule);
            setScheduleControlValue('runEvenIfNotifications', schedule.runEvenIfNotifications);
            dialog.querySelectorAll('[data-schedule-field]').forEach(control => { control.disabled = isScheduleReadOnly(schedule); });
            refreshScheduleDependencies(schedule);
            updateScheduleSummaries(schedule);
            setScheduleTab(scheduleTabName);
            setScheduleDayMode(dayMode);
        }

        function renderScheduleDays(schedule) {
            dialog.querySelectorAll('[data-schedule-weekday]').forEach(button => {
                const value = String(button.dataset.scheduleWeekday || '');
                button.classList.toggle('selected', (schedule.daysOfWeek || []).some(day => String(day).toLowerCase() === value.toLowerCase()));
                button.setAttribute('aria-pressed', button.classList.contains('selected') ? 'true' : 'false');
                button.disabled = isScheduleReadOnly(schedule);
            });
            dialog.querySelectorAll('[data-schedule-monthday]').forEach(button => {
                const day = Number(button.dataset.scheduleMonthday || 0);
                button.classList.toggle('selected', (schedule.daysOfMonth || []).includes(day));
                button.setAttribute('aria-pressed', button.classList.contains('selected') ? 'true' : 'false');
                button.disabled = isScheduleReadOnly(schedule);
            });
        }

        function renderScheduleDateLists(schedule) {
            if (!scheduleDateListRows) return;
            scheduleDateListRows.replaceChildren();
            const references = schedule?.dateLists || [];
            if (references.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'traditional-task-editor-schedule-date-list-empty';
                empty.textContent = scheduleHasCrossingInterval(schedule)
                    ? 'MOVEit Date List schedules must remain within the selected date and cannot span midnight.'
                    : (dateListOptions.length
                        ? 'No Date Lists are referenced by this schedule.'
                        : 'No MOVEit Date Lists are available to the current user.');
                scheduleDateListRows.appendChild(empty);
                return;
            }

            const fragment = document.createDocumentFragment();
            references.forEach(reference => {
                const row = document.createElement('div');
                row.className = 'traditional-task-editor-schedule-date-list-row';
                row.dataset.scheduleDateListKey = reference.key;

                const select = document.createElement('select');
                select.className = 'text-input';
                select.dataset.scheduleDateListField = 'id';
                select.appendChild(new Option('Select Date List…', ''));
                dateListOptions.forEach(option => select.appendChild(new Option(option.name, option.id)));
                ensureSelectOption(select, reference.id, reference.name || `Date List (ID ${reference.id})`);
                select.value = reference.id;
                select.disabled = isScheduleReadOnly(schedule) || scheduleHasCrossingInterval(schedule);

                const mode = document.createElement('select');
                mode.className = 'text-input';
                mode.dataset.scheduleDateListField = 'run';
                mode.append(new Option('Include', 'true'), new Option('Exclude', 'false'));
                mode.value = reference.run ? 'true' : 'false';
                mode.disabled = isScheduleReadOnly(schedule) || scheduleHasCrossingInterval(schedule);

                const remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'secondary-button compact-button';
                remove.textContent = 'Remove';
                remove.dataset.scheduleDateListRemove = reference.key;
                remove.disabled = isScheduleReadOnly(schedule) || scheduleHasCrossingInterval(schedule);

                row.append(select, mode, remove);
                fragment.appendChild(row);
            });
            scheduleDateListRows.appendChild(fragment);
        }

        let scheduleTimeMenu = null;
        let scheduleTimeMenuInput = null;
        let scheduleTimeMenuToggle = null;

        function closeScheduleTimeMenu() {
            if (!scheduleTimeMenu) return;
            scheduleTimeMenu.hidden = true;
            scheduleTimeMenu.style.removeProperty('left');
            scheduleTimeMenu.style.removeProperty('top');
            scheduleTimeMenu.style.removeProperty('width');
            if (scheduleTimeMenuToggle) scheduleTimeMenuToggle.setAttribute('aria-expanded', 'false');
            scheduleTimeMenuInput = null;
            scheduleTimeMenuToggle = null;
        }

        function ensureScheduleTimeMenu() {
            if (scheduleTimeMenu?.isConnected) return scheduleTimeMenu;

            const menu = document.createElement('div');
            menu.className = 'traditional-task-editor-time-menu';
            menu.hidden = true;
            menu.setAttribute('role', 'listbox');
            menu.setAttribute('aria-label', 'Schedule time choices');

            // Native-like convenience list: every five minutes for the full day. The text input
            // remains free-form, so values such as 12:03 AM are still accepted and validated.
            for (let minute = 0; minute < 1440; minute += 5) {
                const hour = Math.floor(minute / 60);
                const mins = minute % 60;
                const normalized = `${String(hour).padStart(2, '0')}:${String(mins).padStart(2, '0')}`;
                const display = formatScheduleTime(normalized);
                const option = document.createElement('button');
                option.type = 'button';
                option.className = 'traditional-task-editor-time-option';
                option.dataset.scheduleTimeChoice = normalized;
                option.textContent = display;
                option.setAttribute('role', 'option');
                option.addEventListener('click', () => {
                    const input = scheduleTimeMenuInput;
                    if (!input || input.disabled) {
                        closeScheduleTimeMenu();
                        return;
                    }
                    input.value = display;
                    closeScheduleTimeMenu();
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                    input.focus();
                });
                menu.appendChild(option);
            }

            dialog.appendChild(menu);
            scheduleTimeMenu = menu;
            return menu;
        }

        function openScheduleTimeMenu(input, toggle) {
            if (!(input instanceof HTMLInputElement) || input.disabled) return;
            const menu = ensureScheduleTimeMenu();
            if (!menu.hidden && scheduleTimeMenuInput === input) {
                closeScheduleTimeMenu();
                return;
            }

            closeScheduleTimeMenu();
            scheduleTimeMenuInput = input;
            scheduleTimeMenuToggle = toggle;
            toggle?.setAttribute('aria-expanded', 'true');

            const combo = input.closest('.traditional-task-editor-time-combo') || input;
            const rect = combo.getBoundingClientRect();
            const viewportPadding = 8;
            const menuHeight = Math.min(320, Math.max(180, window.innerHeight - (viewportPadding * 2)));
            const roomBelow = window.innerHeight - rect.bottom - viewportPadding;
            const roomAbove = rect.top - viewportPadding;

            menu.style.visibility = 'hidden';
            menu.hidden = false;
            menu.style.width = `${Math.max(170, rect.width)}px`;
            menu.style.left = `${Math.min(Math.max(viewportPadding, rect.left), Math.max(viewportPadding, window.innerWidth - rect.width - viewportPadding))}px`;
            if (roomBelow >= Math.min(menuHeight, 220) || roomBelow >= roomAbove) {
                menu.style.top = `${Math.min(window.innerHeight - viewportPadding - Math.min(menu.scrollHeight, menuHeight), rect.bottom + 4)}px`;
            } else {
                menu.style.top = `${Math.max(viewportPadding, rect.top - Math.min(menu.scrollHeight, menuHeight) - 4)}px`;
            }
            menu.style.visibility = '';

            const current = normalizeScheduleTime(input.value);
            menu.querySelectorAll('[data-schedule-time-choice]').forEach(option => {
                const selected = option.dataset.scheduleTimeChoice === current;
                option.classList.toggle('is-current', selected);
                option.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            const selected = menu.querySelector('.traditional-task-editor-time-option.is-current');
            if (selected) selected.scrollIntoView({ block: 'center' });
            else menu.scrollTop = 0;
        }

        function createScheduleTimeCombo(interval, field, disabled) {
            const combo = document.createElement('div');
            combo.className = 'traditional-task-editor-time-combo';

            const input = document.createElement('input');
            input.type = 'text';
            input.className = 'text-input';
            input.autocomplete = 'off';
            input.placeholder = '12:00 AM';
            input.dataset.scheduleIntervalField = field;
            input.value = formatScheduleTime(interval[field]);
            input.disabled = Boolean(disabled);
            input.setAttribute('aria-haspopup', 'listbox');
            input.addEventListener('keydown', event => {
                if ((event.key === 'ArrowDown' && event.altKey) || event.key === 'F4') {
                    event.preventDefault();
                    openScheduleTimeMenu(input, toggle);
                } else if (event.key === 'Escape' && !scheduleTimeMenu?.hidden) {
                    event.preventDefault();
                    closeScheduleTimeMenu();
                }
            });

            const toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'traditional-task-editor-time-toggle';
            toggle.disabled = Boolean(disabled);
            toggle.setAttribute('aria-label', `Show ${field === 'startTime' ? 'Start Time' : 'End Time'} choices`);
            toggle.setAttribute('aria-haspopup', 'listbox');
            toggle.setAttribute('aria-expanded', 'false');
            toggle.innerHTML = '<span aria-hidden="true">▾</span>';
            toggle.addEventListener('click', event => {
                event.preventDefault();
                event.stopPropagation();
                openScheduleTimeMenu(input, toggle);
            });

            combo.append(input, toggle);
            return { combo, input };
        }

        dialog.addEventListener('pointerdown', event => {
            if (!scheduleTimeMenu || scheduleTimeMenu.hidden) return;
            if (scheduleTimeMenu.contains(event.target)) return;
            if (event.target.closest?.('.traditional-task-editor-time-combo')) return;
            closeScheduleTimeMenu();
        });
        window.addEventListener('resize', closeScheduleTimeMenu);

        function renderScheduleIntervals(schedule) {
            if (!scheduleIntervals) return;
            closeScheduleTimeMenu();
            scheduleIntervals.replaceChildren();
            const intervals = schedule?.intervals || [];
            if (intervals.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'traditional-task-editor-schedule-date-list-empty';
                empty.textContent = 'No scheduled times are defined.';
                scheduleIntervals.appendChild(empty);
                return;
            }

            const fragment = document.createDocumentFragment();
            intervals.forEach((interval, index) => {
                const card = document.createElement('div');
                card.className = 'traditional-task-editor-schedule-interval';
                card.dataset.scheduleIntervalKey = interval.key;

                const grid = document.createElement('div');
                grid.className = 'traditional-task-editor-schedule-interval-grid';

                const startLabel = document.createElement('label');
                startLabel.className = 'field-block';
                const startCaption = document.createElement('span');
                startCaption.textContent = index === 0 ? 'Start Time' : `Start Time ${index + 1}`;
                const startTimeCombo = createScheduleTimeCombo(interval, 'startTime', isScheduleReadOnly(schedule));
                const start = startTimeCombo.input;
                startLabel.append(startCaption, startTimeCombo.combo);

                const repeatLabel = document.createElement('label');
                repeatLabel.className = 'traditional-task-editor-schedule-repeat';
                const repeat = document.createElement('input');
                repeat.type = 'checkbox';
                repeat.dataset.scheduleIntervalField = 'repeat';
                repeat.checked = Boolean(interval.repeat);
                repeat.disabled = isScheduleReadOnly(schedule);
                repeat.title = '';
                repeatLabel.append(repeat, document.createTextNode('Repeat'));

                const everyLabel = document.createElement('label');
                everyLabel.className = `field-block${interval.repeat ? '' : ' is-disabled-value'}`;
                const everyCaption = document.createElement('span');
                everyCaption.textContent = 'Every (minutes)';
                const every = document.createElement('input');
                every.type = 'number';
                every.min = '1';
                every.className = 'text-input';
                every.dataset.scheduleIntervalField = 'everyMinutes';
                every.disabled = !interval.repeat || isScheduleReadOnly(schedule);
                every.value = interval.everyMinutes > 0 ? String(interval.everyMinutes) : '';
                everyLabel.append(everyCaption, every);

                const endLabel = document.createElement('label');
                endLabel.className = `field-block${interval.repeat ? '' : ' is-disabled-value'}`;
                const endCaption = document.createElement('span');
                endCaption.textContent = 'End Time';
                const endTimeCombo = createScheduleTimeCombo(interval, 'endTime', !interval.repeat || isScheduleReadOnly(schedule));
                const end = endTimeCombo.input;
                endLabel.append(endCaption, endTimeCombo.combo);

                grid.append(startLabel, repeatLabel, everyLabel, endLabel);

                if (intervals.length > 1) {
                    const remove = document.createElement('button');
                    remove.type = 'button';
                    remove.className = 'secondary-button compact-button traditional-task-editor-schedule-interval-remove';
                    remove.textContent = 'Remove Time';
                    remove.dataset.scheduleIntervalRemove = interval.key;
                    remove.disabled = isScheduleReadOnly(schedule);
                    card.append(grid, remove);
                } else {
                    card.appendChild(grid);
                }
                fragment.appendChild(card);
            });
            scheduleIntervals.appendChild(fragment);
        }

        function setScheduleControlValue(field, value) {
            const control = dialog.querySelector(`[data-schedule-field="${field}"]`);
            if (!control) return;
            if (control instanceof HTMLInputElement && control.type === 'checkbox') control.checked = Boolean(value);
            else control.value = value ?? '';
        }

        function setScheduleTab(name) {
            scheduleTabName = ['days', 'times', 'options'].includes(name) ? name : 'days';
            scheduleTabs.forEach(tab => {
                const selected = tab.dataset.traditionalTaskEditorScheduleTab === scheduleTabName;
                tab.classList.toggle('active', selected);
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            schedulePanels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorSchedulePanel !== scheduleTabName; });
        }

        function setScheduleDayMode(name) {
            scheduleDayMode = ['week', 'month', 'custom'].includes(name) ? name : 'week';
            const schedule = activeSchedule();
            if (schedule) schedule.uiDayMode = scheduleDayMode;
            scheduleDayModeButtons.forEach(button => {
                const selected = button.dataset.traditionalTaskEditorScheduleDayMode === scheduleDayMode;
                button.classList.toggle('active', selected);
                button.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            scheduleDayPanels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorScheduleDayPanel !== scheduleDayMode; });
        }

        function refreshScheduleDependencies(schedule) {
            const hasRepeat = (schedule?.intervals || []).some(interval => interval.repeat);
            const firstSuccess = dialog.querySelector('[data-schedule-field="onlyUntilFirstSuccess"]');
            if (firstSuccess) firstSuccess.disabled = isScheduleReadOnly(schedule) || !hasRepeat;
        }

        function addSchedule() {
            const key = `temp-schedule:${++temporaryScheduleSequence}`;
            const intervalKey = `temp-schedule-interval:${++temporaryScheduleIntervalSequence}`;
            const schedule = {
                key,
                index: schedules.length,
                serverIndex: -1,
                id: '',
                multi: 0,
                daysOfWeek: [],
                daysOfMonth: [],
                dateLists: [],
                uiDayMode: 'week',
                intervals: [{ key: intervalKey, serverIndex: -1, id: '', startTime: '00:00', endTime: '23:59', everyMinutes: 15, repeat: false }],
                onlyUntilFirstSuccess: false,
                failIfNoSuccessInSchedule: false,
                runEvenIfNotifications: false
            };
            schedules.push(schedule);
            activeScheduleKey = key;
            lastUndo = { kind: 'schedule-add', scheduleKey: key, label: 'Add Schedule' };
            setWorkspace('schedules');
            renderScheduleRail();
            selectSchedule(key);
            updateUndoState();
            updateDirtyState();
        }

        function removeActiveSchedule() {
            if (isScheduleReadOnly(activeSchedule())) return;
            const index = schedules.findIndex(item => item.key === activeScheduleKey);
            if (index < 0) return;
            const removed = deepClone(schedules[index]);
            schedules.splice(index, 1);
            normalizeScheduleIndexes();
            lastUndo = { kind: 'schedule-remove', schedule: removed, position: index, label: 'Remove Schedule' };
            activeScheduleKey = schedules[index]?.key || schedules[index - 1]?.key || '';
            renderScheduleRail();
            if (activeScheduleKey) selectSchedule(activeScheduleKey); else showNoSchedule();
            updateUndoState();
            updateDirtyState();
        }

        function cloneActiveSchedule() {
            const source = activeSchedule();
            if (!source || isScheduleReadOnly(source)) return;
            const clone = deepClone(source);
            clone.key = `temp-schedule:${++temporaryScheduleSequence}`;
            clone.index = schedules.length;
            clone.cloneFromServerIndex = source.serverIndex >= 0 ? source.serverIndex : (source.cloneFromServerIndex ?? -1);
            clone.serverIndex = -1;
            clone.id = '';
            if (isPairedOvernightSchedule(source)) {
                delete clone.overnightPair;
                delete clone.pairedServerIndex;
                delete clone.pairedId;
                clone.multi = 0;
            }
            clone.intervals = (clone.intervals || []).map(interval => {
                const copy = {
                    ...interval,
                    key: `temp-schedule-interval:${++temporaryScheduleIntervalSequence}`,
                    serverIndex: interval.serverIndex ?? -1,
                    id: ''
                };
                delete copy.pairedServerIndex;
                delete copy.pairedId;
                delete copy.splitPreEndTime;
                delete copy.splitPostStartTime;
                delete copy.originalStartTime;
                delete copy.originalEveryMinutes;
                return copy;
            });
            clone.dateLists = (clone.dateLists || []).map(reference => ({ ...reference }));
            schedules.push(clone);
            activeScheduleKey = clone.key;
            lastUndo = { kind: 'schedule-add', scheduleKey: clone.key, label: 'Clone Schedule' };
            renderScheduleRail();
            selectSchedule(clone.key);
            updateUndoState();
            updateDirtyState();
        }

        function toggleScheduleActionsMenu() {
            if (!scheduleActionsMenu || !scheduleActionsButton || !activeSchedule()) return;
            closeActionsMenu(); closeStepActionsMenu(); closeNextActionActionsMenu();
            const show = scheduleActionsMenu.hidden;
            scheduleActionsMenu.hidden = !show;
            scheduleActionsButton.setAttribute('aria-expanded', show ? 'true' : 'false');
        }

        function closeScheduleActionsMenu() {
            if (scheduleActionsMenu) scheduleActionsMenu.hidden = true;
            if (scheduleActionsButton) scheduleActionsButton.setAttribute('aria-expanded', 'false');
        }

        function handleScheduleAction(action) {
            closeScheduleActionsMenu();
            if (action === 'clone') cloneActiveSchedule();
            else if (action === 'delete') removeActiveSchedule();
        }

        function toggleScheduleWeekday(day) {
            const schedule = activeSchedule();
            if (!schedule || isScheduleReadOnly(schedule) || !day) return;
            const before = deepClone(schedule);
            const existingIndex = schedule.daysOfWeek.findIndex(value => String(value).toLowerCase() === String(day).toLowerCase());
            if (existingIndex >= 0) schedule.daysOfWeek.splice(existingIndex, 1);
            else schedule.daysOfWeek.push(day);
            schedule.daysOfWeek = orderedScheduleWeekdays(schedule.daysOfWeek);
            commitScheduleSnapshot(schedule, before, `${existingIndex >= 0 ? 'Remove' : 'Add'} ${day}`);
        }

        function toggleScheduleMonthDay(day) {
            const schedule = activeSchedule();
            const numericDay = Number(day);
            if (!schedule || isScheduleReadOnly(schedule) || !Number.isInteger(numericDay) || numericDay < 1 || numericDay > 31) return;
            const before = deepClone(schedule);
            const existingIndex = schedule.daysOfMonth.indexOf(numericDay);
            if (existingIndex >= 0) schedule.daysOfMonth.splice(existingIndex, 1);
            else schedule.daysOfMonth.push(numericDay);
            schedule.daysOfMonth.sort((a, b) => a - b);
            commitScheduleSnapshot(schedule, before, `${existingIndex >= 0 ? 'Remove' : 'Add'} day ${numericDay}`);
        }

        function orderedScheduleWeekdays(values) {
            const order = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
            return order.filter(day => (values || []).some(value => String(value).toLowerCase() === day.toLowerCase()));
        }

        function addScheduleDateList() {
            const schedule = activeSchedule();
            if (!schedule || isScheduleReadOnly(schedule)) return;
            if (scheduleHasCrossingInterval(schedule)) {
                setFooterStatus('MOVEit Date List schedules must remain within the selected date and cannot span midnight.');
                return;
            }
            if (dateListOptions.length === 0) {
                setFooterStatus('No MOVEit Date Lists are available to add.');
                return;
            }
            const before = deepClone(schedule);
            schedule.dateLists.push({
                key: `temp-schedule-date-list:${++temporaryScheduleDateListSequence}`,
                id: '',
                name: '',
                run: true
            });
            commitScheduleSnapshot(schedule, before, 'Add Date List');
            setScheduleDayMode('custom');
        }

        function removeScheduleDateList(referenceKey) {
            const schedule = activeSchedule();
            if (!schedule || isScheduleReadOnly(schedule) || scheduleHasCrossingInterval(schedule)) return;
            const index = schedule.dateLists.findIndex(reference => reference.key === referenceKey);
            if (index < 0) return;
            const before = deepClone(schedule);
            schedule.dateLists.splice(index, 1);
            commitScheduleSnapshot(schedule, before, 'Remove Date List');
        }

        function applyScheduleDateListControl(control) {
            const schedule = activeSchedule();
            const row = control.closest?.('[data-schedule-date-list-key]');
            const reference = schedule?.dateLists.find(item => item.key === row?.dataset.scheduleDateListKey);
            const field = control.dataset.scheduleDateListField || '';
            if (!schedule || isScheduleReadOnly(schedule) || scheduleHasCrossingInterval(schedule) || !reference || !field) return;
            const before = deepClone(schedule);
            if (field === 'run') reference.run = String(control.value).toLowerCase() === 'true';
            else if (field === 'id') {
                reference.id = String(control.value || '');
                reference.name = dateListName(reference.id);
            }
            commitScheduleSnapshot(schedule, before, field === 'id' ? 'Date List' : 'Date List Include/Exclude');
        }

        function addScheduleTime() {
            const schedule = activeSchedule();
            if (!schedule || isScheduleReadOnly(schedule) || scheduleHasCrossingInterval(schedule)) return;
            const before = deepClone(schedule);
            schedule.intervals.push({
                key: `temp-schedule-interval:${++temporaryScheduleIntervalSequence}`,
                serverIndex: -1,
                id: '',
                startTime: '00:00',
                endTime: '23:59',
                everyMinutes: 15,
                repeat: false
            });
            commitScheduleSnapshot(schedule, before, 'Add Schedule Time');
            setScheduleTab('times');
        }

        function removeScheduleTime(intervalKey) {
            const schedule = activeSchedule();
            if (!schedule || isScheduleReadOnly(schedule) || schedule.intervals.length <= 1) return;
            const index = schedule.intervals.findIndex(interval => interval.key === intervalKey);
            if (index < 0) return;
            const before = deepClone(schedule);
            schedule.intervals.splice(index, 1);
            commitScheduleSnapshot(schedule, before, 'Remove Schedule Time');
        }

        function applyScheduleIntervalControl(control) {
            const schedule = activeSchedule();
            const card = control.closest?.('[data-schedule-interval-key]');
            const interval = schedule?.intervals.find(item => item.key === card?.dataset.scheduleIntervalKey);
            const field = control.dataset.scheduleIntervalField || '';
            if (!schedule || isScheduleReadOnly(schedule) || !interval || !field) return;
            const before = deepClone(schedule);

            if (field === 'repeat') {
                interval.repeat = Boolean(control.checked);
                if (interval.repeat && Number(interval.everyMinutes || 0) <= 0) {
                    interval.everyMinutes = 15;
                } else if (!interval.repeat) {
                    interval.everyMinutes = 0;
                }
            } else if (field === 'everyMinutes') {
                interval.everyMinutes = Math.max(0, Number(control.value || 0) || 0);
            } else if (field === 'startTime') {
                interval.startTime = normalizeScheduleTime(control.value);
            } else if (field === 'endTime') {
                interval.endTime = normalizeScheduleTime(control.value);
            }
            commitScheduleSnapshot(schedule, before, scheduleIntervalFieldLabel(field));
        }

        function scheduleIntervalFieldLabel(field) {
            const labels = { repeat: 'Repeat', everyMinutes: 'Repeat Interval', startTime: 'Start Time', endTime: 'End Time' };
            return labels[field] || 'Schedule Time';
        }

        function applyScheduleOptionControl(control) {
            const schedule = activeSchedule();
            const field = control.dataset.scheduleField || '';
            if (!schedule || isScheduleReadOnly(schedule) || !field) return;
            const before = deepClone(schedule);
            schedule[field] = control instanceof HTMLInputElement && control.type === 'checkbox' ? control.checked : control.value;
            commitScheduleSnapshot(schedule, before, scheduleOptionFieldLabel(field));
        }

        function scheduleOptionFieldLabel(field) {
            const labels = {
                onlyUntilFirstSuccess: 'Repeat only until first success',
                failIfNoSuccessInSchedule: 'Log failure if no files found',
                runEvenIfNotifications: 'Poll when file notifications are enabled'
            };
            return labels[field] || 'Schedule Option';
        }

        function commitScheduleSnapshot(schedule, before, label) {
            if (!schedule || sameValue(before, schedule)) return;
            lastUndo = { kind: 'schedule-snapshot', scheduleKey: schedule.key, before, label };
            renderScheduleRail();
            renderSchedule(schedule);
            updateUndoState();
            updateDirtyState();
        }

        function dateListName(id) {
            const match = dateListOptions.find(option => String(option.id) === String(id || ''));
            return match?.name || '';
        }

        function normalizeNextActionIndexes() {
            nextActions.forEach((action, index) => { action.index = index; });
        }

        function renderNextActionRail() {
            if (isAdvancedTask()) { advancedEditor?.refresh(); return; }
            if (!nextActionItems) return;
            nextActionItems.replaceChildren();
            normalizeNextActionIndexes();
            setText('[data-traditional-task-editor-next-action-count]', `(${nextActions.length})`);
            if (nextActions.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'traditional-task-editor-empty-item';
                empty.textContent = 'No Next Actions are currently defined.';
                nextActionItems.appendChild(empty);
                return;
            }

            const fragment = document.createDocumentFragment();
            nextActions.forEach((action, index) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'traditional-task-editor-step-item traditional-task-editor-next-action-item';
                button.classList.toggle('active', action.key === activeNextActionKey);
                button.dataset.traditionalTaskEditorNextActionKey = action.key;
                const number = document.createElement('span');
                number.className = 'traditional-task-editor-step-number';
                number.textContent = String(index + 1);
                const copy = document.createElement('span');
                copy.className = 'traditional-task-editor-step-copy';
                const name = document.createElement('strong');
                name.textContent = nextActionTypeLabel(action.type);
                const detail = document.createElement('small');
                detail.textContent = nextActionRailDetail(action);
                copy.append(name, detail);
                button.append(number, copy);
                fragment.appendChild(button);
            });
            nextActionItems.appendChild(fragment);
        }

        function nextActionTypeLabel(type) {
            const normalized = String(type || '').toLowerCase();
            if (normalized === 'email') return 'Send Email';
            if (normalized === 'task') return 'Run Task';
            return type || 'Next Action';
        }

        function nextActionConditionLabel(action) {
            const labels = [];
            if (action?.doIfSuccess) labels.push('Success');
            if (action?.doIfFailure) labels.push('Failure');
            if (action?.doIfNoAction) labels.push('No Action');
            return labels.length ? labels.join(' + ') : 'No result selected';
        }

        function nextActionRailDetail(action) {
            const type = String(action?.type || '').toLowerCase();
            if (type === 'email') {
                const subject = String(action?.subject || '').trim();
                return subject || nextActionConditionLabel(action);
            }
            if (type === 'task') return action?.taskName || taskOptionName(action?.taskId) || 'Select a Task';
            return nextActionConditionLabel(action);
        }

        function nextActionSummaryText(action) {
            if (isAdvancedTask()) {
                if (String(action?.type || '').toLowerCase() === 'email') return `${String(action?.subject || '').trim() || 'Subject not set'} · ${smtpHostName(action.hostId) || action.hostName || 'SMTP Host not selected'}`;
                return taskOptionName(action?.taskId) || action?.taskName || 'Task not selected';
            }
            const when = String(action?.doAfter || '').toLowerCase() === 'filedone' ? 'after each file' : 'after task';
            const conditions = nextActionConditionLabel(action);
            const type = String(action?.type || '').toLowerCase();
            if (type === 'email') {
                const host = smtpHostName(action.hostId) || action.hostName || 'SMTP Host not selected';
                return `${conditions} · ${when} · ${host}`;
            }
            if (type === 'task') {
                const target = taskOptionName(action.taskId) || action.taskName || 'Task not selected';
                return `${conditions} · ${when} · ${target}`;
            }
            return `${conditions} · ${when}`;
        }

        function smtpHostName(id) {
            const match = smtpHosts.find(item => String(item.id) === String(id || ''));
            return match?.name || '';
        }

        function taskOptionName(id) {
            const match = taskOptions.find(item => String(item.id) === String(id || ''));
            return match?.name || '';
        }

        function activeNextAction() {
            return nextActions.find(item => item.key === activeNextActionKey) || null;
        }

        function selectNextAction(actionKey) {
            const action = nextActions.find(item => item.key === actionKey);
            activeNextActionKey = action?.key || '';
            renderNextActionRail();
            if (!action) { showNoNextAction(); return; }

            if (nextActionEmpty) nextActionEmpty.hidden = true;
            if (nextActionDetail) nextActionDetail.hidden = false;
            if (nextActionKicker) { nextActionKicker.hidden = isAdvancedTask(); nextActionKicker.textContent = isAdvancedTask() ? '' : `Action ${action.index + 1}`; }
            if (nextActionTitle) nextActionTitle.textContent = nextActionTypeLabel(action.type);
            if (nextActionSummary) nextActionSummary.textContent = nextActionSummaryText(action);
            renderNextAction(action);
        }

        function showNoNextAction() {
            activeNextActionKey = '';
            renderNextActionRail();
            if (nextActionEmpty) nextActionEmpty.hidden = false;
            if (nextActionDetail) nextActionDetail.hidden = true;
        }

        function populateNextActionOptions() {
            if (nextActionSmtpHost) {
                nextActionSmtpHost.replaceChildren(new Option('Select SMTP Host…', ''));
                smtpHosts.forEach(host => nextActionSmtpHost.appendChild(new Option(host.name, host.id)));
            }
            if (nextActionTask) {
                nextActionTask.replaceChildren(new Option('Select Task…', ''));
                taskOptions
                    .filter(task => String(task.id) !== String(activeTaskId))
                    .forEach(task => nextActionTask.appendChild(new Option(task.name, task.id)));
            }
            buildNextActionMacroOptions(nextActionSubjectMacro);
            buildNextActionMacroOptions(nextActionBodyMacro);
            buildNextActionMacroOptions(destinationSmtpRecipientMacro);
            buildNextActionMacroOptions(destinationSmtpSubjectMacro);
            buildNextActionMacroOptions(destinationSmtpBodyMacro);
            fieldMacroSelects.forEach(buildFieldMacroOptions);
        }

        function renderNextAction(action) {
            const normalizedType = String(action?.type || '').toLowerCase();
            const isEmail = normalizedType === 'email';
            const isRunTask = normalizedType === 'task';
            nextActionEmailGeneral.forEach(element => { element.hidden = !isEmail; });
            nextActionRunTaskGeneral.forEach(element => { element.hidden = !isRunTask; });

            if (nextActionSmtpHost) ensureSelectOption(nextActionSmtpHost, action.hostId, action.hostName || `SMTP Host (ID ${action.hostId})`);
            if (nextActionTask) {
                if (String(action.taskId || '') === String(activeTaskId) && action.taskId) {
                    if (![...nextActionTask.options].some(option => option.value === String(action.taskId))) {
                        const invalidCurrent = new Option(`${action.taskName || activeTaskName || 'Current Task'} (current task - select another)`, String(action.taskId));
                        invalidCurrent.disabled = true;
                        nextActionTask.appendChild(invalidCurrent);
                    }
                } else {
                    ensureSelectOption(nextActionTask, action.taskId, action.taskName || `Task (ID ${action.taskId})`);
                }
            }
            setNextActionControlValue('wait', Boolean(action.wait));
            const wait = dialog.querySelector('[data-advanced-wait]'); if (wait) wait.hidden = !isAdvancedTask() || !isRunTask;
            setNextActionControlValue('hostId', action.hostId);
            setNextActionControlValue('addressTo', action.addressTo);
            setNextActionControlValue('taskId', action.taskId);
            setNextActionControlValue('subject', action.subject);
            setNextActionControlValue('doIfSuccess', action.doIfSuccess);
            setNextActionControlValue('doIfFailure', action.doIfFailure);
            setNextActionControlValue('doIfNoAction', action.doIfNoAction);
            setNextActionControlValue('doAfter', action.doAfter || 'Task');
            if (nextActionFormat) nextActionFormat.value = action.isHtml ? 'html' : 'text';
            nextActionHtmlSourceMode = false;
            setNextActionMessageContent(action);
            renderNextActionParameters(action);

            nextActionTabs.forEach(tab => {
                const name = tab.dataset.traditionalTaskEditorNextActionTab || '';
                tab.hidden = (name === 'message' && !isEmail) || (name === 'parameters' && !isRunTask);
            });
            const conditions = dialog.querySelector('[data-traditional-task-editor-next-action-conditions]');
            if (conditions) conditions.hidden = isAdvancedTask();
            if ((nextActionTabName === 'message' && !isEmail) || (nextActionTabName === 'parameters' && !isRunTask)) nextActionTabName = 'general';
            setNextActionTab(nextActionTabName);
        }

        function ensureSelectOption(select, value, label) {
            const normalized = String(value || '');
            if (!select || !normalized) return;
            if (![...select.options].some(option => option.value === normalized)) select.appendChild(new Option(label || normalized, normalized));
        }

        function setNextActionControlValue(field, value) {
            const controls = Array.from(dialog.querySelectorAll(`[data-next-action-field="${field}"]`));
            controls.forEach(control => {
                if (control instanceof HTMLInputElement && control.type === 'checkbox') control.checked = Boolean(value);
                else if (control instanceof HTMLInputElement && control.type === 'radio') control.checked = String(control.value) === String(value);
                else if ('value' in control) control.value = value ?? '';
            });
        }

        function renderNextActionParameters(action) {
            renderParameterRows(action?.parameters, nextActionParameterRows, nextActionParameterTable, nextActionParametersEmpty, {
                editDatasetKey: 'traditionalTaskEditorNextActionParameterEdit',
                removeDatasetKey: 'traditionalTaskEditorNextActionParameterRemove'
            });
        }

        function setNextActionTab(tabName) {
            nextActionTabName = tabName || 'general';
            nextActionTabs.forEach(tab => {
                const selected = tab.dataset.traditionalTaskEditorNextActionTab === nextActionTabName;
                tab.classList.toggle('active', selected);
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            nextActionPanels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorNextActionPanel !== nextActionTabName; });
        }

        function addNextAction(type) {
            closeAddNextActionMenu();
            const normalizedType = String(type || '').toLowerCase() === 'task' ? 'Task' : 'Email';
            const key = `local-next-action:${++temporaryNextActionSequence}`;
            const action = normalizeNextAction({
                index: nextActions.length,
                serverIndex: -1,
                id: '',
                type: normalizedType,
                doAfter: 'Task',
                doIfSuccess: true,
                doIfFailure: false,
                doIfNoAction: false,
                isHtml: normalizedType === 'Email',
                hostId: '', hostName: '', addressTo: '', subject: '', message: '', taskId: '', taskName: '', parameters: []
            });
            action.key = key;
            nextActions.push(action);
            normalizeNextActionIndexes();
            if (isAdvancedTask()) advancedEditor?.attach(action, normalizedType === 'Email' ? 'Email' : 'RunTask');
            seedNextActionStableValues(action);
            if (!isAdvancedTask()) lastUndo = { kind: 'next-action-add', actionKey: key, label: `Add ${nextActionTypeLabel(normalizedType)}` };
            activeNextActionKey = key;
            renderNextActionRail();
            selectNextAction(key);
            setNextActionTab('general');
            updateUndoState();
            updateDirtyState();
        }

        function removeActiveNextAction() {
            if (isAdvancedTask()) { void advancedEditor?.remove(); return; }
            const index = nextActions.findIndex(action => action.key === activeNextActionKey);
            if (index < 0) return;
            const removed = deepClone(nextActions[index]);
            nextActions.splice(index, 1);
            normalizeNextActionIndexes();
            lastUndo = { kind: 'next-action-remove', action: removed, position: index, label: `Remove ${nextActionTypeLabel(removed.type)}` };
            nextActionStableValues.forEach((_, key) => { if (key.startsWith(`${removed.key}|`)) nextActionStableValues.delete(key); });
            const next = nextActions[index] || nextActions[index - 1] || null;
            activeNextActionKey = next?.key || '';
            renderNextActionRail();
            if (next) selectNextAction(next.key); else showNoNextAction();
            updateUndoState();
            updateDirtyState();
        }

        function cloneActiveNextAction() {
            if (isAdvancedTask()) { advancedEditor?.copy(); return; }
            const source = activeNextAction();
            if (!source) return;
            if ((source.parameters || []).some(parameter => parameter.secure && !parameter.valueChanged)) {
                setFooterStatus('This Next Action cannot be cloned until its secure parameter values are explicitly changed.', 'error');
                return;
            }
            const clone = deepClone(source);
            clone.key = `local-next-action:${++temporaryNextActionSequence}`;
            clone.index = nextActions.length;
            clone.cloneFromServerIndex = source.serverIndex >= 0 ? source.serverIndex : (source.cloneFromServerIndex ?? -1);
            clone.serverIndex = -1;
            clone.id = '';
            clone.parameters = (clone.parameters || []).map(parameter => ({ ...parameter, originalName: parameter.name }));
            nextActions.push(clone);
            normalizeNextActionIndexes();
            seedNextActionStableValues(clone);
            activeNextActionKey = clone.key;
            lastUndo = { kind: 'next-action-add', actionKey: clone.key, label: `Clone ${nextActionTypeLabel(clone.type)}` };
            renderNextActionRail();
            selectNextAction(clone.key);
            updateUndoState();
            updateDirtyState();
        }

        function toggleNextActionActionsMenu() {
            if (!nextActionActionsMenu || !nextActionActionsButton || !activeNextAction()) return;
            closeActionsMenu(); closeStepActionsMenu(); closeScheduleActionsMenu();
            const show = nextActionActionsMenu.hidden;
            nextActionActionsMenu.hidden = !show;
            nextActionActionsButton.setAttribute('aria-expanded', show ? 'true' : 'false');
        }

        function closeNextActionActionsMenu() {
            if (nextActionActionsMenu) nextActionActionsMenu.hidden = true;
            if (nextActionActionsButton) nextActionActionsButton.setAttribute('aria-expanded', 'false');
        }

        function handleNextActionAction(action) {
            closeNextActionActionsMenu();
            if (action === 'clone') cloneActiveNextAction();
            else if (action === 'delete') removeActiveNextAction();
        }

        // Reuse the same MOVEit field macro definitions as Quick Transfer.
        function buildFieldMacroOptions(select) {
            window.MauiTaskFieldMacros.populateSelect(select, select?.dataset?.macroContext || '');
        }

        function applyFieldMacro(select) {
            const macro = String(select?.value || '');
            const selector = String(select?.dataset?.macroTarget || '');
            select.value = '';
            if (!macro || !selector) return;
            const input = dialog.querySelector(selector);
            if (!(input instanceof HTMLInputElement || input instanceof HTMLTextAreaElement)) return;
            insertTextAtCaret(input, macro);
            // Use the normal editor event path so the active Step model, dirty state, and one-step
            // Undo all see macro insertion exactly like a user edit.
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }

        const nextActionMacros = [
            ['Task', '[TaskID]'], ['Task', '[TaskName]'], ['Task', '[TaskStatus]'],
            ['Errors', '[ErrorCodeTask]'], ['Errors', '[ErrorDescriptionTask]'], ['Errors', '[ErrorCodeFile]'], ['Errors', '[ErrorDescriptionFile]'],
            ['File', '[OrigName]'], ['File', '[OnlyName]'], ['File', '[OnlyExt]'], ['File', '[FullPath]'], ['File', '[DestFileName]'], ['File', '[DestFolderPath]'], ['File', '[FileSize]'], ['File', '[FileDateStamp]'],
            ['Run', '[NominalStart]'],
            ['Date/Time', '[yyyy]'], ['Date/Time', '[mm]'], ['Date/Time', '[dd]'], ['Date/Time', '[hh]'], ['Date/Time', '[tt]'], ['Date/Time', '[ss]']
        ];

        function buildNextActionMacroOptions(select) {
            if (!select) return;
            select.replaceChildren();
            const first = document.createElement('option'); first.value = ''; first.textContent = 'Macros…'; select.appendChild(first);
            let currentGroup = null;
            let group = null;
            nextActionMacros.forEach(([category, macro]) => {
                if (category !== currentGroup) {
                    group = document.createElement('optgroup'); group.label = category; select.appendChild(group); currentGroup = category;
                }
                const option = document.createElement('option'); option.value = macro; option.textContent = macro; group.appendChild(option);
            });
        }

        function populateNextActionTemplates() {
            if (!nextActionTemplateMenu) return;
            nextActionTemplateMenu.replaceChildren();
            const items = notificationTemplates.slice().sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }));
            if (!items.length) {
                const empty = document.createElement('div'); empty.className = 'task-notification-template-empty'; empty.textContent = 'No saved templates'; nextActionTemplateMenu.appendChild(empty); return;
            }
            items.forEach(template => {
                const button = document.createElement('button');
                button.type = 'button';
                button.setAttribute('role', 'menuitem');
                button.textContent = template.name || template.id || 'Template';
                button.addEventListener('click', () => {
                    const action = activeNextAction();
                    if (!action || String(action.type).toLowerCase() !== 'email') return;
                    const before = deepClone(action);
                    templateSelections.set(`next-action:${action.key}`, template.id);
                    action.isHtml = template.format ? template.format !== 'text' : /<[a-z][\s\S]*>/i.test(template.message || '');
                    action.subject = template.subject || '';
                    action.message = template.message || '';
                    seedNextActionStableValues(action);
                    lastUndo = { kind: 'next-action-snapshot', actionKey: action.key, before, label: `Load Template ${template.name || ''}`.trim() };
                    nextActionTemplateMenu.hidden = true;
                    renderNextAction(action);
                    renderNextActionRail();
                    updateUndoState();
                    updateDirtyState();
                });
                nextActionTemplateMenu.appendChild(button);
            });
        }

        function populateDestinationSmtpTemplates() {
            if (!destinationSmtpTemplateMenu) return;
            destinationSmtpTemplateMenu.replaceChildren();
            const items = notificationTemplates.slice().sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }));
            if (!items.length) {
                const empty = document.createElement('div');
                empty.className = 'task-notification-template-empty';
                empty.textContent = 'No saved templates';
                destinationSmtpTemplateMenu.appendChild(empty);
                return;
            }
            items.forEach(template => {
                const button = document.createElement('button');
                button.type = 'button';
                button.setAttribute('role', 'menuitem');
                button.textContent = template.name || template.id || 'Template';
                button.addEventListener('click', () => {
                    const step = activeStep();
                    const destination = activeSmtpDestination();
                    if (!step || !destination) return;
                    const before = deepClone(destination);
                    templateSelections.set(`smtp:${step.key}`, template.id);
                    destination.isHtml = template.format ? template.format !== 'text' : /<[a-z][\s\S]*>/i.test(template.message || '');
                    destination.subject = template.subject || '';
                    destination.message = template.message || '';
                    destinationSmtpTemplateMenu.hidden = true;
                    lastUndo = { kind: 'destination-snapshot', stepKey: step.key, before, label: `Load Template ${template.name || ''}`.trim() };
                    seedDestinationStableValues(step);
                    setDestinationControlValue('subject', destination.subject);
                    setDestinationControlValue('isHtml', destination.isHtml ? '1' : '0');
                    setDestinationSmtpMessageContent(destination);
                    updateUndoState();
                    updateDirtyState();
                });
                destinationSmtpTemplateMenu.appendChild(button);
            });
        }

        function sanitizeNextActionStyle(styleText) {
            const allowedProperties = new Set([
                'color', 'background-color', 'font-size', 'font-family', 'font-weight', 'font-style', 'text-decoration', 'text-align',
                'border', 'border-width', 'border-style', 'border-color', 'border-collapse',
                'padding', 'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
                'margin', 'margin-top', 'margin-right', 'margin-bottom', 'margin-left'
            ]);
            const probe = document.createElement('span');
            probe.style.cssText = String(styleText || '');
            const safe = [];
            Array.from(probe.style).forEach(property => {
                const normalized = String(property || '').toLowerCase();
                if (!allowedProperties.has(normalized)) return;
                const value = String(probe.style.getPropertyValue(property) || '').trim();
                if (!value || /(?:url\s*\(|expression\s*\(|javascript:|@import|behavior\s*:)/i.test(value)) return;
                safe.push(`${normalized}: ${value}`);
            });
            return safe.join('; ');
        }

        function sanitizeNextActionHtml(html) {
            const template = document.createElement('template'); template.innerHTML = String(html || '');
            const allowed = new Set(['P','DIV','SPAN','BR','STRONG','B','EM','I','U','UL','OL','LI','A','TABLE','THEAD','TBODY','TFOOT','TR','TH','TD']);
            const numericAttributes = new Set(['colspan', 'rowspan', 'cellpadding', 'cellspacing', 'border']);
            const dimensionAttributes = new Set(['width', 'height']);
            const clean = node => {
                Array.from(node.children).forEach(child => {
                    clean(child);
                    if (!allowed.has(child.tagName)) { child.replaceWith(...Array.from(child.childNodes)); return; }
                    Array.from(child.attributes).forEach(attr => {
                        const name = attr.name.toLowerCase();
                        const value = String(attr.value || '').trim();
                        if (child.tagName === 'A' && name === 'href') {
                            if (!/^(https?:|mailto:)/i.test(value)) child.removeAttribute(attr.name);
                            return;
                        }
                        if (name === 'style') {
                            const safeStyle = sanitizeNextActionStyle(value);
                            if (safeStyle) child.setAttribute('style', safeStyle); else child.removeAttribute(attr.name);
                            return;
                        }
                        if (numericAttributes.has(name) && ['TABLE','TH','TD'].includes(child.tagName)) {
                            if (!/^\d{1,4}$/.test(value)) child.removeAttribute(attr.name);
                            return;
                        }
                        if (dimensionAttributes.has(name) && ['TABLE','TH','TD'].includes(child.tagName)) {
                            if (!/^\d{1,4}(?:px|%)?$/i.test(value)) child.removeAttribute(attr.name);
                            return;
                        }
                        if (name === 'align' && ['TABLE','TR','TH','TD','P','DIV'].includes(child.tagName)) {
                            if (!/^(left|center|right|justify)$/i.test(value)) child.removeAttribute(attr.name);
                            return;
                        }
                        if (name === 'valign' && ['TR','TH','TD'].includes(child.tagName)) {
                            if (!/^(top|middle|bottom|baseline)$/i.test(value)) child.removeAttribute(attr.name);
                            return;
                        }
                        child.removeAttribute(attr.name);
                    });
                });
            };
            clean(template.content);
            return template.innerHTML;
        }

        function setNextActionMessageContent(action) {
            if (!action) return;
            const html = Boolean(action.isHtml);
            if (nextActionHtmlEditor) nextActionHtmlEditor.innerHTML = html ? sanitizeNextActionHtml(action.message || '') : '';
            if (nextActionHtmlSource) nextActionHtmlSource.value = html ? sanitizeNextActionHtml(action.message || '') : '';
            if (nextActionTextEditor) nextActionTextEditor.value = html ? '' : String(action.message || '');
            updateNextActionFormatUi();
        }

        function currentNextActionMessage() {
            const action = activeNextAction();
            if (!action) return '';
            if (!action.isHtml) return String(nextActionTextEditor?.value || '');
            const raw = nextActionHtmlSourceMode ? nextActionHtmlSource?.value || '' : nextActionHtmlEditor?.innerHTML || '';
            return sanitizeNextActionHtml(raw);
        }

        function setNextActionHtmlSourceMode(sourceMode, focus = true) {
            const action = activeNextAction();
            if (!action?.isHtml || !nextActionHtmlEditor || !nextActionHtmlSource) return;
            if (sourceMode) nextActionHtmlSource.value = sanitizeNextActionHtml(nextActionHtmlEditor.innerHTML || '');
            else {
                const sanitized = sanitizeNextActionHtml(nextActionHtmlSource.value || '');
                nextActionHtmlSource.value = sanitized;
                nextActionHtmlEditor.innerHTML = sanitized;
                action.message = sanitized;
            }
            nextActionHtmlSourceMode = Boolean(sourceMode);
            updateNextActionFormatUi();
            if (focus) {
                if (nextActionHtmlSourceMode) nextActionHtmlSource.focus();
                else { nextActionHtmlEditor.focus(); rememberNextActionHtmlRange(); }
            }
        }

        function updateNextActionFormatUi() {
            const action = activeNextAction();
            const html = Boolean(action?.isHtml);
            if (nextActionHtmlToolbar) {
                nextActionHtmlToolbar.hidden = false;
                nextActionHtmlToolbar.querySelectorAll('[data-next-action-command],[data-traditional-task-editor-next-action-link],[data-traditional-task-editor-next-action-html-toggle]').forEach(control => { control.hidden = !html; });
            }
            if (nextActionHtmlEditor) nextActionHtmlEditor.hidden = !html || nextActionHtmlSourceMode;
            if (nextActionHtmlSource) nextActionHtmlSource.hidden = !html || !nextActionHtmlSourceMode;
            if (nextActionTextEditor) nextActionTextEditor.hidden = html;
            if (nextActionHtmlToggle) {
                nextActionHtmlToggle.textContent = nextActionHtmlSourceMode ? 'Visual' : 'Source';
                nextActionHtmlToggle.title = nextActionHtmlSourceMode ? 'Return to visual editor' : 'Edit HTML source';
                nextActionHtmlToggle.classList.toggle('is-active', nextActionHtmlSourceMode);
            }
        }

        function rememberNextActionHtmlRange() {
            const selection = window.getSelection();
            if (selection?.rangeCount && nextActionHtmlEditor?.contains(selection.anchorNode)) nextActionHtmlRange = selection.getRangeAt(0).cloneRange();
        }

        function restoreNextActionHtmlRange() {
            if (!nextActionHtmlEditor) return false;
            nextActionHtmlEditor.focus();
            const selection = window.getSelection();
            if (!selection || !nextActionHtmlRange) return false;
            try { selection.removeAllRanges(); selection.addRange(nextActionHtmlRange); return true; } catch { return false; }
        }

        function insertTextAtCaret(input, text) {
            if (!input) return;
            const start = Number.isFinite(input.selectionStart) ? input.selectionStart : input.value.length;
            const end = Number.isFinite(input.selectionEnd) ? input.selectionEnd : start;
            input.setRangeText(text, start, end, 'end');
            input.focus();
        }

        function insertNextActionHtmlMacro(text) {
            if (!nextActionHtmlEditor) return;
            restoreNextActionHtmlRange();
            const selection = window.getSelection();
            if (!selection?.rangeCount || !nextActionHtmlEditor.contains(selection.anchorNode)) {
                nextActionHtmlEditor.append(document.createTextNode(text)); rememberNextActionHtmlRange(); return;
            }
            const range = selection.getRangeAt(0); range.deleteContents(); const node = document.createTextNode(text); range.insertNode(node);
            range.setStartAfter(node); range.collapse(true); selection.removeAllRanges(); selection.addRange(range); rememberNextActionHtmlRange();
        }

        function activeSmtpDestination() {
            const step = activeStep();
            const destination = step?.destination;
            return destination && hostWriteProfile('Destination', destination.hostTypeCode) === HOST_WRITE_PROFILE_SMTP ? destination : null;
        }

        function setDestinationSmtpMessageContent(destination) {
            if (!destination) return;
            const html = Boolean(destination.isHtml);
            const sanitized = html ? sanitizeNextActionHtml(destination.message || '') : '';
            if (destinationSmtpHtmlEditor) destinationSmtpHtmlEditor.innerHTML = sanitized;
            if (destinationSmtpHtmlSource) destinationSmtpHtmlSource.value = sanitized;
            if (destinationSmtpTextEditor) destinationSmtpTextEditor.value = html ? '' : String(destination.message || '');
            destinationSmtpHtmlSourceMode = false;
            updateDestinationSmtpFormatUi(destination);
        }

        function currentDestinationSmtpMessage() {
            const destination = activeSmtpDestination();
            if (!destination) return '';
            if (!destination.isHtml) return String(destinationSmtpTextEditor?.value || '');
            const raw = destinationSmtpHtmlSourceMode
                ? destinationSmtpHtmlSource?.value || ''
                : destinationSmtpHtmlEditor?.innerHTML || '';
            return sanitizeNextActionHtml(raw);
        }

        function updateDestinationSmtpFormatUi(destination = activeSmtpDestination()) {
            const html = Boolean(destination?.isHtml);
            if (destinationSmtpHtmlToolbar) {
                destinationSmtpHtmlToolbar.hidden = false;
                destinationSmtpHtmlToolbar.querySelectorAll('[data-destination-smtp-command],[data-traditional-task-editor-destination-smtp-link],[data-traditional-task-editor-destination-smtp-html-toggle]').forEach(control => { control.hidden = !html; });
            }
            if (destinationSmtpHtmlEditor) destinationSmtpHtmlEditor.hidden = !html || destinationSmtpHtmlSourceMode;
            if (destinationSmtpHtmlSource) destinationSmtpHtmlSource.hidden = !html || !destinationSmtpHtmlSourceMode;
            if (destinationSmtpTextEditor) destinationSmtpTextEditor.hidden = html;
            if (destinationSmtpHtmlToggle) {
                destinationSmtpHtmlToggle.textContent = destinationSmtpHtmlSourceMode ? 'Visual' : 'Source';
                destinationSmtpHtmlToggle.title = destinationSmtpHtmlSourceMode ? 'Return to visual editor' : 'Edit HTML source';
                destinationSmtpHtmlToggle.classList.toggle('is-active', destinationSmtpHtmlSourceMode);
            }
        }

        function rememberDestinationSmtpHtmlRange() {
            const selection = window.getSelection();
            if (selection?.rangeCount && destinationSmtpHtmlEditor?.contains(selection.anchorNode))
                destinationSmtpHtmlRange = selection.getRangeAt(0).cloneRange();
        }

        function restoreDestinationSmtpHtmlRange() {
            if (!destinationSmtpHtmlEditor) return false;
            destinationSmtpHtmlEditor.focus();
            const selection = window.getSelection();
            if (!selection || !destinationSmtpHtmlRange) return false;
            try {
                selection.removeAllRanges();
                selection.addRange(destinationSmtpHtmlRange);
                return true;
            } catch {
                return false;
            }
        }

        function insertDestinationSmtpHtmlMacro(text) {
            if (!destinationSmtpHtmlEditor) return;
            restoreDestinationSmtpHtmlRange();
            const selection = window.getSelection();
            if (!selection?.rangeCount || !destinationSmtpHtmlEditor.contains(selection.anchorNode)) {
                destinationSmtpHtmlEditor.append(document.createTextNode(text));
                rememberDestinationSmtpHtmlRange();
                return;
            }
            const range = selection.getRangeAt(0);
            range.deleteContents();
            const node = document.createTextNode(text);
            range.insertNode(node);
            range.setStartAfter(node);
            range.collapse(true);
            selection.removeAllRanges();
            selection.addRange(range);
            rememberDestinationSmtpHtmlRange();
        }

        function updateDestinationSmtpMessage(commit) {
            const step = activeStep();
            const destination = activeSmtpDestination();
            if (!step || !destination) return;
            const field = 'message';
            const key = destinationStableKey(step.key, field);
            const before = destinationStableValues.has(key) ? destinationStableValues.get(key) : cloneSimple(destination.message);
            const after = currentDestinationSmtpMessage();
            destination.message = destination.isHtml ? sanitizeNextActionHtml(after) : after;
            if (commit && !sameValue(before, destination.message)) {
                lastUndo = { kind: 'destination-field', stepKey: step.key, field, before, label: 'Message' };
                seedDestinationStableValues(step);
                updateUndoState();
            }
            updateStepSummary(step);
            updateDirtyState();
        }

        function setDestinationSmtpHtmlSourceMode(sourceMode, focus = true) {
            const destination = activeSmtpDestination();
            if (!destination?.isHtml || !destinationSmtpHtmlEditor || !destinationSmtpHtmlSource) return;
            if (sourceMode) destinationSmtpHtmlSource.value = sanitizeNextActionHtml(destinationSmtpHtmlEditor.innerHTML || '');
            else {
                const sanitized = sanitizeNextActionHtml(destinationSmtpHtmlSource.value || '');
                destinationSmtpHtmlSource.value = sanitized;
                destinationSmtpHtmlEditor.innerHTML = sanitized;
                destination.message = sanitized;
            }
            destinationSmtpHtmlSourceMode = Boolean(sourceMode);
            updateDestinationSmtpFormatUi(destination);
            if (focus) {
                if (destinationSmtpHtmlSourceMode) destinationSmtpHtmlSource.focus();
                else {
                    destinationSmtpHtmlEditor.focus();
                    rememberDestinationSmtpHtmlRange();
                }
            }
        }

        function applyDestinationSmtpMessageMutation(label, mutation) {
            const step = activeStep();
            const destination = activeSmtpDestination();
            if (!step || !destination) return;
            const before = currentDestinationSmtpMessage();
            mutation();
            const after = currentDestinationSmtpMessage();
            destination.message = destination.isHtml ? sanitizeNextActionHtml(after) : after;
            if (!sameValue(before, destination.message)) {
                lastUndo = { kind: 'destination-field', stepKey: step.key, field: 'message', before, label };
                seedDestinationStableValues(step);
                updateUndoState();
                updateDirtyState();
            }
        }

        function applyDestinationSmtpFormat(control) {
            const step = activeStep();
            const destination = activeSmtpDestination();
            if (!step || !destination || !(control instanceof HTMLInputElement) || control.type !== 'radio' || !control.checked) return;
            const toHtml = String(control.value || '') === '1';
            if (Boolean(destination.isHtml) === toHtml) return;
            const before = deepClone(destination);
            const current = currentDestinationSmtpMessage();
            destination.isHtml = toHtml;
            destinationSmtpHtmlSourceMode = false;
            if (toHtml) {
                if (destinationSmtpHtmlEditor) {
                    destinationSmtpHtmlEditor.replaceChildren();
                    String(current).split(/\r?\n/).forEach((line, index) => {
                        if (index) destinationSmtpHtmlEditor.appendChild(document.createElement('br'));
                        destinationSmtpHtmlEditor.appendChild(document.createTextNode(line));
                    });
                }
                destination.message = sanitizeNextActionHtml(destinationSmtpHtmlEditor?.innerHTML || '');
            } else {
                const text = destinationSmtpHtmlEditor?.innerText ?? destinationSmtpHtmlEditor?.textContent ?? current;
                destination.message = String(text || '');
            }
            lastUndo = { kind: 'destination-snapshot', stepKey: step.key, before, label: 'Message Format' };
            seedDestinationStableValues(step);
            renderDestination(step);
            updateUndoState();
            updateDirtyState();
        }

        function nextActionFieldKey(action, field) {
            return `${action.key}|${field}`;
        }

        function seedNextActionStableValues(action) {
            if (!action?.key) return;
            const values = {
                hostId: action.hostId || '', addressTo: action.addressTo || '', taskId: action.taskId || '', subject: action.subject || '',
                doIfSuccess: Boolean(action.doIfSuccess), doIfFailure: Boolean(action.doIfFailure), doIfNoAction: Boolean(action.doIfNoAction),
                doAfter: action.doAfter || 'Task', message: action.message || '', isHtml: Boolean(action.isHtml)
            };
            Object.entries(values).forEach(([field, value]) => nextActionStableValues.set(nextActionFieldKey(action, field), cloneSimple(value)));
        }

        function canonicalNextActionField(field) {
            if (field === 'message' || field === 'messageSource' || field === 'messageText') return 'message';
            if (field === 'format') return 'isHtml';
            return field;
        }

        function nextActionControlValue(control, action) {
            const rawField = control?.dataset?.nextActionField || '';
            const field = canonicalNextActionField(rawField);
            if (field === 'message') return currentNextActionMessage();
            if (field === 'isHtml') return String(control.value || 'html') === 'html';
            if (control instanceof HTMLInputElement && control.type === 'checkbox') return control.checked;
            if (control instanceof HTMLInputElement && control.type === 'radio') return control.checked ? control.value : action[field];
            return String(control?.value ?? '');
        }

        function applyNextActionControl(control, commit) {
            const action = activeNextAction();
            const rawField = control?.dataset?.nextActionField || '';
            if (!action || !rawField) return;
            const field = canonicalNextActionField(rawField);

            if (field === 'isHtml') {
                if (!commit) return;
                const before = deepClone(action);
                const toHtml = String(control.value || '') === 'html';
                if (Boolean(action.isHtml) === toHtml) return;
                if (toHtml) {
                    const text = String(nextActionTextEditor?.value || action.message || '');
                    if (nextActionHtmlEditor) {
                        nextActionHtmlEditor.replaceChildren();
                        text.split(/\r?\n/).forEach((line, index) => { if (index) nextActionHtmlEditor.appendChild(document.createElement('br')); nextActionHtmlEditor.appendChild(document.createTextNode(line)); });
                    }
                    action.message = sanitizeNextActionHtml(nextActionHtmlEditor?.innerHTML || '');
                } else {
                    action.message = nextActionHtmlEditor?.innerText || '';
                }
                action.isHtml = toHtml;
                nextActionHtmlSourceMode = false;
                seedNextActionStableValues(action);
                lastUndo = { kind: 'next-action-snapshot', actionKey: action.key, before, label: 'Message Format' };
                renderNextAction(action);
                updateUndoState(); updateDirtyState();
                return;
            }

            const key = nextActionFieldKey(action, field);
            const before = nextActionStableValues.has(key) ? cloneSimple(nextActionStableValues.get(key)) : cloneSimple(action[field]);
            const after = nextActionControlValue(control, action);
            if (control instanceof HTMLInputElement && control.type === 'radio' && !control.checked) return;
            action[field] = cloneSimple(after);
            if (field === 'hostId') action.hostName = smtpHostName(after);
            if (field === 'taskId') action.taskName = taskOptionName(after);
            if (field === 'message' && action.isHtml) action.message = sanitizeNextActionHtml(action.message);

            if (commit && !sameValue(before, action[field])) {
                lastUndo = { kind: 'next-action-field', actionKey: action.key, field, before, after: cloneSimple(action[field]), label: nextActionFieldLabel(field) };
                nextActionStableValues.set(key, cloneSimple(action[field]));
                updateUndoState();
            }
            if (nextActionSummary) nextActionSummary.textContent = nextActionSummaryText(action);
            renderNextActionRail();
            updateDirtyState();
        }

        function nextActionFieldLabel(field) {
            const labels = {
                hostId: 'SMTP Host', addressTo: 'Recipients', taskId: 'Task to Run', subject: 'Subject', message: 'Message Body', isHtml: 'Message Format',
                doIfSuccess: 'Success Condition', doIfFailure: 'Failure Condition', doIfNoAction: 'No Action Condition', doAfter: 'Execute After'
            };
            return labels[field] || field;
        }

        function applyNextActionMessageMutation(label, mutate) {
            const action = activeNextAction();
            if (!action || String(action.type).toLowerCase() !== 'email') return;
            const before = action.message || '';
            mutate();
            action.message = currentNextActionMessage();
            const after = action.message;
            if (sameValue(before, after)) return;
            nextActionStableValues.set(nextActionFieldKey(action, 'message'), cloneSimple(after));
            lastUndo = { kind: 'next-action-field', actionKey: action.key, field: 'message', before, after, label };
            updateUndoState(); updateDirtyState();
        }

        function populateAdvanced(advanced) {
            setValue('[data-traditional-task-editor-cache-names]', normalizeCacheNames(advanced.cacheNames));
            const cacheTime = Number(advanced.stateCacheTime ?? 0);
            const cacheMode = cacheTime < 0 ? 'always' : (cacheTime === 0 ? 'task' : 'for');
            if (stateCacheTimeField) stateCacheTimeField.value = String(Number.isFinite(cacheTime) ? cacheTime : 0);
            if (stateCacheForValue) stateCacheForValue.value = cacheTime > 0 ? String(cacheTime) : '0';
            setSelectValue('[data-traditional-task-editor-state-cache-unit]', advanced.stateCacheUnit || 'Minutes');
            stateCacheModeControls.forEach(control => { control.checked = control.value === cacheMode; });
            setChecked('[data-traditional-task-editor-use-default-state-cache]', Boolean(advanced.useDefaultStateCaching));
            setChecked('[data-traditional-task-editor-auto-rerun]', Boolean(advanced.autoRerun));
            const useGlobalLogs = advanced.taskLogsToKeep == null;
            setChecked('[data-traditional-task-editor-use-global-task-log-retention]', useGlobalLogs);
            setValue('[data-traditional-task-editor-logs-to-keep]', advanced.taskLogsToKeep ?? '');
            refreshAdvancedDependencies();
        }

        function selectedStateCacheMode() {
            return stateCacheModeControls.find(control => control.checked)?.value || 'task';
        }

        function commitStateCacheMode() {
            if (!stateCacheTimeField) return;
            const before = stableValues.has('stateCacheTime') ? stableValues.get('stateCacheTime') : readControl(stateCacheTimeField);
            const mode = selectedStateCacheMode();
            if (mode === 'always') stateCacheTimeField.value = '-1';
            else if (mode === 'task') stateCacheTimeField.value = '0';
            else {
                let amount = Number(stateCacheForValue?.value || 0);
                if (!Number.isFinite(amount) || amount <= 0) amount = 5;
                if (stateCacheForValue) stateCacheForValue.value = String(Math.trunc(amount));
                stateCacheTimeField.value = String(Math.trunc(amount));
            }
            const after = readControl(stateCacheTimeField);
            if (before !== after) {
                lastUndo = { kind: 'field', key: 'stateCacheTime', before, after, label: 'State Cache Duration' };
                stableValues.set('stateCacheTime', after);
                updateUndoState();
            }
            refreshAdvancedDependencies();
            updateDirtyState();
        }

        function commitStateCacheForValue() {
            if (!stateCacheTimeField || selectedStateCacheMode() !== 'for') return;
            let amount = Number(stateCacheForValue?.value || 0);
            if (!Number.isFinite(amount) || amount <= 0) amount = 5;
            stateCacheForValue.value = String(Math.trunc(amount));
            const before = stableValues.has('stateCacheTime') ? stableValues.get('stateCacheTime') : readControl(stateCacheTimeField);
            stateCacheTimeField.value = String(Math.trunc(amount));
            const after = readControl(stateCacheTimeField);
            if (before !== after) {
                lastUndo = { kind: 'field', key: 'stateCacheTime', before, after, label: 'State Cache Duration' };
                stableValues.set('stateCacheTime', after);
                updateUndoState();
            }
            updateDirtyState();
        }

        function refreshAdvancedDependencies() {
            const useDefault = useDefaultStateCache?.checked ?? true;
            if (stateCacheSettings) stateCacheSettings.hidden = useDefault;
            stateCacheModeControls.forEach(control => { control.disabled = useDefault; });
            const useFor = selectedStateCacheMode() === 'for';
            if (stateCacheForValue) stateCacheForValue.disabled = useDefault || !useFor;
            if (stateCacheUnit) stateCacheUnit.disabled = useDefault || !useFor;

            const useGlobalLogs = useGlobalTaskLogRetention?.checked ?? true;
            if (taskLogRetentionSettings) taskLogRetentionSettings.hidden = useGlobalLogs;
            if (taskLogsToKeepInput) taskLogsToKeepInput.disabled = useGlobalLogs;
            if (taskLogRetentionWarning) taskLogRetentionWarning.hidden = useGlobalLogs;
        }

        function populateFolders(folders, selectedFolderId) {
            if (!folderSelect) return;
            folderSelect.replaceChildren(new Option('All Tasks', ''));
            (folders || []).forEach(folder => {
                const option = new Option(folder.displayName || folder.name || `Folder ${folder.id}`, String(folder.id));
                folderSelect.appendChild(option);
            });
            folderSelect.value = selectedFolderId == null ? '' : String(selectedFolderId);
        }

        function populateMetadataFields(fields, values) {
            if (!metadataFields) return;
            const fragment = document.createDocumentFragment();
            (fields || []).forEach(field => {
                const label = document.createElement('label');
                const multiline = String(field.type || '').toLowerCase().includes('multiline');
                label.className = `field-block${multiline ? ' full-span' : ''}`;
                const caption = document.createElement('span');
                caption.textContent = field.label || field.name;
                if (field.description) caption.title = field.description;
                label.appendChild(caption);

                const control = createMetadataControl(field, values?.[field.name] ?? '');
                control.dataset.editorField = `metadata:${field.name}`;
                control.dataset.metadataField = field.name;
                control.required = Boolean(field.required);
                window.mauiConfigureMetadataControl?.(control, field);
                label.appendChild(control);
                fragment.appendChild(label);
            });
            metadataFields.replaceChildren(fragment);
        }

        function createMetadataControl(field, value) {
            const type = String(field.type || 'String').toLowerCase();
            if (type === 'multilinestring' || type === 'multiline') {
                const textarea = document.createElement('textarea');
                textarea.className = 'text-input textarea-input';
                textarea.rows = 4;
                textarea.value = value;
                return textarea;
            }
            if (type === 'boolean') {
                const select = document.createElement('select');
                select.className = 'text-input';
                select.append(new Option('False', 'false'), new Option('True', 'true'));
                select.value = String(value).toLowerCase() === 'true' ? 'true' : 'false';
                return select;
            }
            if (type === 'list') {
                const select = document.createElement('select');
                select.className = 'text-input';
                (field.possibleValues || []).forEach(optionValue => select.appendChild(new Option(optionValue, optionValue)));
                if (![...select.options].some(option => option.value === String(value)) && String(value)) select.appendChild(new Option(String(value), String(value)));
                select.value = String(value);
                return select;
            }
            const input = document.createElement('input');
            input.className = 'text-input';
            input.type = type === 'numeric' || type === 'number' ? 'number' : type === 'date' ? 'date' : 'text';
            input.value = value;
            return input;
        }

        function populateAudit(audit) {
            if (!auditHost) return;
            const rows = [
                ['Created On', formatDate(audit.createdUtc)],
                ['Created By', audit.createdBy || '—'],
                ['Modified On', formatDate(audit.modifiedUtc)],
                ['Modified By', audit.modifiedBy || '—']
            ];
            const fragment = document.createDocumentFragment();
            rows.forEach(([labelText, valueText]) => {
                const item = document.createElement('label');
                item.className = 'audit-item';
                const label = document.createElement('span');
                label.textContent = labelText;
                const value = document.createElement('input'); value.type = 'text'; value.className = 'text-input'; value.readOnly = true;
                value.value = valueText || '—';
                item.append(label, value);
                fragment.appendChild(item);
            });
            auditHost.replaceChildren(fragment);
        }

        function normalizeStepOrder() {
            if (isAdvancedTask()) { normalizeStepIndexes(); return; }
            const rank = kind => {
                const normalized = String(kind || '').toLowerCase();
                if (normalized === 'source') return 0;
                if (normalized === 'process') return 1;
                if (normalized === 'destination') return 2;
                return 3;
            };
            taskSteps = taskSteps
                .map((step, position) => ({ step, position }))
                .sort((left, right) => rank(left.step.kind) - rank(right.step.kind) || left.position - right.position)
                .map(item => item.step);
            normalizeStepIndexes();
        }

        function renderStepRail() {
            if (isAdvancedTask()) { advancedEditor?.refresh(); return; }
            if (!stepItems) return;
            stepItems.replaceChildren();
            normalizeStepIndexes();
            setText('[data-traditional-task-editor-step-count]', `(${taskSteps.length})`);
            if (addStepButton) addStepButton.disabled = isSyncTask() && taskSteps.length >= 2;
            if (taskSteps.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'traditional-task-editor-empty-item';
                empty.textContent = isSyncTask() ? 'Add Folder A and Folder B.' : 'No steps are currently defined.';
                stepItems.appendChild(empty);
                return;
            }

            const fragment = document.createDocumentFragment();
            taskSteps.forEach((step, index) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'traditional-task-editor-step-item';
                button.classList.toggle('active', step.key === activeStepKey);
                button.dataset.traditionalTaskEditorStepKey = step.key;
                const number = document.createElement('span');
                number.className = 'traditional-task-editor-step-number';
                number.textContent = String(index + 1);
                const copy = document.createElement('span');
                copy.className = 'traditional-task-editor-step-copy';
                const name = document.createElement('strong');
                name.textContent = isSyncTask() ? `Folder ${index === 0 ? 'A' : 'B'}` : step.kind;
                const detail = document.createElement('small');
                detail.textContent = stepRailDetail(step);
                copy.append(name, detail);
                button.append(number, copy);
                fragment.appendChild(button);
            });
            stepItems.appendChild(fragment);
        }

        function stepRailDetail(step) {
            if (step.kind === 'Source' && step.source) {
                const host = step.source.hostName || step.source.hostType || 'Select a Host';
                if (hostWriteProfile('Source', step.source.hostTypeCode) === HOST_WRITE_PROFILE_POP3)
                    return step.source.fileMask ? `${host} · attachments ${step.source.fileMask}` : host;
                const selection = [step.source.path, step.source.fileMask].filter(Boolean).join(' / ');
                return selection ? `${host} · ${selection}` : host;
            }
            if (step.kind === 'Process' && step.process) {
                const script = step.process.scriptName || 'Select a Script';
                const run = String(step.process.run).toLowerCase() === 'once' ? 'Once after all downloads' : 'Per file';
                return `${script} · ${run}`;
            }
            if (step.kind === 'Destination' && step.destination) {
                const host = step.destination.hostName || step.destination.hostType || 'Select a Host';
                if (hostWriteProfile('Destination', step.destination.hostTypeCode) === HOST_WRITE_PROFILE_SMTP)
                    return step.destination.addressTo ? `${host} · ${step.destination.addressTo}` : host;
                return step.destination.path ? `${host} · ${step.destination.path}` : host;
            }
            return step.subtitle || `${step.kind} step`;
        }

        function selectStep(stepKey) {
            const step = taskSteps.find(item => item.key === stepKey);
            const taskWorkspace = dialog.querySelector('.traditional-task-editor-workspace');
            if (taskWorkspace) taskWorkspace.scrollTop = 0;
            const nextStepKey = step?.key || '';
            const stepChanged = nextStepKey !== activeStepKey;
            if (stepChanged) {
                // MOVEit's native editor opens each Step at its normal first-tab position rather than
                // carrying the previous Step's nested tab selection forward.
                sourceTabName = 'general';
                hostTabName = '';
                processTabName = 'general';
                destinationTabName = 'general';
                destinationHostTabName = '';
            }
            activeStepKey = nextStepKey;
            renderStepRail();
            if (!step) { showNoStep(); return; }

            if (stepEmpty) stepEmpty.hidden = true;
            if (stepDetail) stepDetail.hidden = false;
            if (stepKicker) { stepKicker.hidden = isAdvancedTask(); stepKicker.textContent = isSyncTask() ? 'Sync Folder' : isAdvancedTask() ? '' : `Step ${step.index + 1}`; }
            if (stepTitle) stepTitle.textContent = isSyncTask() ? `Folder ${step.index === 0 ? 'A' : 'B'}` : `${step.kind} Step`;
            if (stepSummary) stepSummary.textContent = stepSummaryText(step);
            configureStepActions(step);

            const isSource = step.kind === 'Source' && Boolean(step.source);
            const isProcess = step.kind === 'Process' && Boolean(step.process);
            const isDestination = step.kind === 'Destination' && Boolean(step.destination);
            if (sourceEditor) sourceEditor.hidden = !isSource;
            if (processEditor) processEditor.hidden = !isProcess;
            if (destinationEditor) destinationEditor.hidden = !isDestination;
            if (genericStep) genericStep.hidden = isSource || isProcess || isDestination;

            if (isSource) renderSource(step);
            else if (isProcess) renderProcess(step);
            else if (isDestination) renderDestination(step);
            else if (genericStepMessage) genericStepMessage.textContent = `${step.kind} properties are not mapped in the MAUI editor. The step remains visible so task order is preserved.`;
        }

        function showNoStep() {
            closeStepActionsMenu();
            activeStepKey = '';
            renderStepRail();
            if (stepEmpty) stepEmpty.hidden = false;
            if (stepDetail) stepDetail.hidden = true;
        }

        function configureStepActions(step) {
            const kind = step?.kind || 'Step';
            const isSource = kind === 'Source' && Boolean(step?.source);
            const isDestination = kind === 'Destination' && Boolean(step?.destination);
            const model = isSource ? step.source : isDestination ? step.destination : null;
            const hostOptions = isSource ? sourceHosts : isDestination ? destinationHosts : [];
            const currentHostType = model?.serverHostTypeCode || model?.hostTypeCode || '';
            const canSelectDifferentHost = Boolean(model?.hostId) && hostOptions.some(host =>
                host.id !== model.hostId && canChangeHost(kind, currentHostType, host.typeCode));
            const hostStepWritable = !model || canWriteHostStep(kind, model.hostTypeCode);
            stepActionButtons.forEach(button => {
                const action = button.dataset.traditionalTaskEditorStepAction || '';
                if (isSyncTask() && action !== 'change-host') { button.hidden = true; button.disabled = true; return; }
                if (action === 'change-host') {
                    button.hidden = !isSyncTask() || !(isSource || isDestination) || !model?.hostId;
                    button.textContent = 'Change Host';
                    button.disabled = !canSelectDifferentHost;
                    button.title = button.disabled && !button.hidden ? 'No verified Host type is available for this Change Host operation.' : '';
                } else if (action === 'clone') {
                    button.hidden = false;
                    button.textContent = `Clone ${kind}`;
                    button.disabled = (isSource || isDestination) && !hostStepWritable;
                    button.title = button.disabled ? `Cloning this ${kind} requires a verified MAUI write profile for its Host type.` : '';
                } else if (action === 'source-timestamps') {
                    button.hidden = !isSource;
                    if (isSource) {
                        const source = step.source;
                        const supported = supportsSourceTimestamps(source.hostTypeCode);
                        const needsSave = supported && source.collectOnlyNewFiles && sourceTimestampSourceNeedsSave(step);
                        button.disabled = saveOutcomeUncertain || !(source.collectOnlyNewFiles && supported) || needsSave;
                        button.title = needsSave
                            ? 'Save this Source before editing Source Timestamps.'
                            : (button.disabled
                                ? 'Source timestamps apply to supported Source types when Collect only new files is enabled.'
                                : '');
                    }
                } else if (action === 'delete') {
                    button.hidden = false;
                    button.textContent = `Delete ${kind}`;
                    button.disabled = false;
                }
            });
        }

        function stepSummaryText(step) {
            if (step.kind === 'Source' && step.source) {
                const source = step.source;
                const host = source.hostName || source.hostType || 'Host not selected';
                const mask = source.fileMask || 'File mask not set';
                if (hostWriteProfile('Source', source.hostTypeCode) === HOST_WRITE_PROFILE_POP3)
                    return `${host} · email attachments · ${mask}`;
                const location = source.path || 'Folder not set';
                return `${host} · ${location} · ${mask}`;
            }
            if (step.kind === 'Process' && step.process) {
                const process = step.process;
                const script = process.scriptName || 'Script not selected';
                const run = String(process.run).toLowerCase() === 'once' ? 'Once after all downloads' : 'Per file';
                return `${script} · ${run}${process.isDestination ? ' · Used as destination' : ''}`;
            }
            if (step.kind === 'Destination' && step.destination) {
                const destination = step.destination;
                const host = destination.hostName || destination.hostType || 'Host not selected';
                if (hostWriteProfile('Destination', destination.hostTypeCode) === HOST_WRITE_PROFILE_SMTP)
                    return `${host} · ${destination.addressTo || 'Recipients not set'}`;
                const location = destination.path || 'Folder not set';
                return `${host} · ${location}`;
            }
            return step.subtitle || '';
        }

        function splitSort(value) {
            const parts = String(value || '').split(',');
            const field = ['Filename', 'Size', 'Date'].includes(parts[0]) ? parts[0] : 'Filename';
            const direction = String(parts[1] || 'A').toUpperCase() === 'D' ? 'D' : 'A';
            return { field, direction };
        }

        function populateClientCertificateSelect(kind, model) {
            const selector = kind === 'Source' ? '[data-source-field="clientCertificateSha1"]' : '[data-destination-field="clientCertificateSha1"]';
            const select = dialog.querySelector(selector);
            if (!(select instanceof HTMLSelectElement)) return;
            select.replaceChildren(new Option('- None -', ''));
            tlsCertificates.forEach(certificate => {
                const suffix = certificate.expirationDate ? ` — expires ${certificate.expirationDate}` : '';
                const option = new Option(`${certificate.name || certificate.sha1Thumbprint}${suffix}`, certificate.sha1Thumbprint);
                option.dataset.store = certificate.store || 'My';
                option.dataset.subject = certificate.name || '';
                select.appendChild(option);
            });
            const currentSha1 = String(model?.clientCertificateSha1 || '');
            const normalizedCurrent = currentSha1.replaceAll(' ', '').toLowerCase();
            const matching = tlsCertificates.find(item => item.sha1Thumbprint.replaceAll(' ', '').toLowerCase() === normalizedCurrent);
            if (currentSha1 && !matching) {
                const option = new Option(model?.clientCertificateSubject || `Current certificate ${currentSha1}`, currentSha1);
                option.dataset.store = model?.clientCertificateStore || 'My';
                option.dataset.subject = model?.clientCertificateSubject || '';
                select.appendChild(option);
            }
            select.value = matching?.sha1Thumbprint || currentSha1;
        }

        function certificateBySha1(value) {
            const normalized = String(value || '').replaceAll(' ', '').toLowerCase();
            if (!normalized) return null;
            return tlsCertificates.find(item => String(item?.sha1Thumbprint || '').replaceAll(' ', '').toLowerCase() === normalized) || null;
        }

        function populateSharePointCertificateSelect(kind, model) {
            const selector = kind === 'Source' ? '[data-source-field="sharePointCertificateSha1"]' : '[data-destination-field="sharePointCertificateSha1"]';
            const select = dialog.querySelector(selector);
            if (!(select instanceof HTMLSelectElement)) return;
            select.replaceChildren(new Option('- None -', ''));
            tlsCertificates.forEach(certificate => {
                const suffix = certificate.expirationDate ? ` — expires ${certificate.expirationDate}` : '';
                const option = new Option(`${certificate.name || certificate.sha1Thumbprint}${suffix}`, certificate.sha1Thumbprint);
                option.dataset.store = certificate.store || 'My';
                option.dataset.subject = certificate.name || '';
                select.appendChild(option);
            });
            const currentSha1 = String(model?.sharePointCertificateSha1 || model?.clientCertificateSha1 || '');
            const normalizedCurrent = currentSha1.replaceAll(' ', '').toLowerCase();
            const matching = tlsCertificates.find(item => item.sha1Thumbprint.replaceAll(' ', '').toLowerCase() === normalizedCurrent);
            if (currentSha1 && !matching) {
                const option = new Option(model?.clientCertificateSubject || `Current certificate ${currentSha1}`, currentSha1);
                option.dataset.store = model?.clientCertificateStore || 'My';
                option.dataset.subject = model?.clientCertificateSubject || '';
                select.appendChild(option);
            }
            select.value = matching?.sha1Thumbprint || currentSha1;
        }

        function populateClientKeySelect(kind, model) {
            const selector = kind === 'Source' ? '[data-source-field="clientKeyId"]' : '[data-destination-field="clientKeyId"]';
            const select = dialog.querySelector(selector);
            if (!(select instanceof HTMLSelectElement)) return;
            select.replaceChildren(new Option('- None -', '0'));
            sshClientKeys.forEach(key => select.appendChild(new Option(key.name || `SSH Key ${key.id}`, key.id)));
            const currentId = String(model?.clientKeyId || model?.hostDefaults?.clientKeyId || '0');
            if (currentId && currentId !== '0' && !sshClientKeys.some(key => String(key.id) === currentId))
                select.appendChild(new Option(`Current SSH Key ID ${currentId}`, currentId));
            select.value = currentId || '0';
        }

        function setDependentControls(kind, fields, enabled) {
            fields.forEach(field => {
                const selector = kind === 'Source' ? `[data-source-field="${field}"]` : `[data-destination-field="${field}"]`;
                dialog.querySelectorAll(selector).forEach(control => {
                    control.disabled = !enabled;
                });
            });
        }

        function clearHostStepWriteAvailability(kind) {
            const editor = kind === 'Source' ? sourceEditor : destinationEditor;
            if (!editor) return;
            editor.querySelectorAll('[data-host-profile-readonly="true"]').forEach(control => {
                control.disabled = false;
                delete control.dataset.hostProfileReadonly;
            });
        }

        function applyHostStepWriteAvailability(kind, model) {
            if (!model) return;
            const writable = !model.hostId || canWriteHostStep(kind, model.hostTypeCode);
            if (writable) return;
            const editor = kind === 'Source' ? sourceEditor : destinationEditor;
            if (!editor) return;
            const fieldSelector = kind === 'Source' ? '[data-source-field]' : '[data-destination-field]';
            const fieldAttribute = kind === 'Source' ? 'sourceField' : 'destinationField';
            const markReadOnly = control => {
                if (!control) return;
                control.disabled = true;
                control.dataset.hostProfileReadonly = 'true';
            };
            editor.querySelectorAll(fieldSelector).forEach(control => {
                const field = control.dataset[fieldAttribute] || '';
                if (field !== 'hostId') markReadOnly(control);
            });
            editor.querySelectorAll('[data-traditional-task-editor-field-macro]').forEach(markReadOnly);
            if (kind === 'Source') {
                markReadOnly(sourceBrowseButton);
                markReadOnly(sourceBucketBrowseButton);
                markReadOnly(sourceContainerBrowseButton);
                markReadOnly(specialFiltersButton);
            } else {
                markReadOnly(destinationBrowseButton);
                markReadOnly(destinationBucketBrowseButton);
                markReadOnly(destinationContainerBrowseButton);
            }
        }

        function renderSource(step) {
            const source = step.source;
            clearHostStepWriteAvailability('Source');
            if (!source) return;
            populateSourceHostSelect(source);
            setSourceTab(sourceTabName);

            setSourceControlValue('path', source.path);
            setSourceControlValue('bucket', source.useDefaultBucket ? source.hostDefaults.bucket : source.bucket);
            setSourceControlValue('container', source.useDefaultContainer ? source.hostDefaults.container : source.container);
            setSourceControlValue('documentLibrary', source.useDefaultDocumentLibrary ? source.hostDefaults.documentLibrary : source.documentLibrary);
            setSourceControlValue('sharePointAuthenticationMode', effectiveOverrideValue(source.overrideCredentials, source.sharePointAuthenticationMode, source.hostDefaults.sharePointAuthenticationMode) ?? 2);
            setSourceControlValue('sharePointClientId', effectiveOverrideValue(source.overrideCredentials, source.sharePointClientId, source.hostDefaults.sharePointClientId));
            setSourceControlValue('sharePointTenantId', effectiveOverrideValue(source.overrideCredentials, source.sharePointTenantId, source.hostDefaults.sharePointTenantId));
            setSourceControlValue('fileMask', source.fileMask);
            setSourceControlValue('ignoreFolders', source.ignoreFolders);
            setSourceControlValue('excludeFolders', source.excludeFolders);
            setSourceControlValue('searchSubdirectories', source.searchSubdirectories);
            setSourceControlValue('ignoreFiles', source.ignoreFiles);
            setSourceControlValue('excludeFiles', source.excludeFiles);
            setSourceControlValue('hasSpecialFilters', source.hasSpecialFilters);
            setSourceControlValue('collectOnlyNewFiles', source.collectOnlyNewFiles);
            setSourceControlValue('expandZipFiles', source.expandZipFiles);
            setSourceControlValue('retryIfNoFilesFound', source.retryIfNoFilesFound);
            setSourceControlValue('deleteOriginalAction', source.deleteOriginalAction);
            setSourceControlValue('renameTo', source.renameTo);
            setSourceControlValue('renamedFilesMayOverwrite', source.renamedFilesMayOverwrite);
            setSourceControlValue('overrideRetryCount', source.overrideRetryCount);
            setSourceControlValue('retryCount', effectiveOverrideValue(source.overrideRetryCount, source.retryCount, source.hostDefaults.retryCount));
            setSourceControlValue('overrideRetryTimeout', source.overrideRetryTimeout);
            setSourceControlValue('retryTimeoutSeconds', effectiveOverrideValue(source.overrideRetryTimeout, source.retryTimeoutSeconds, source.hostDefaults.retryTimeoutSeconds));
            setSourceControlValue('overrideRescanTime', source.overrideRescanTime);
            setSourceControlValue('rescanSeconds', effectiveOverrideValue(source.overrideRescanTime, source.rescanSeconds, source.hostDefaults.rescanSeconds));
            setSourceControlValue('overrideMaxFiles', source.overrideMaxFiles);
            setSourceControlValue('maxFiles', effectiveOverrideValue(source.overrideMaxFiles, source.maxFiles, source.hostDefaults.maxFiles));
            setSourceControlValue('overrideMaxBytes', source.overrideMaxBytes);
            setSourceControlValue('maxBytes', effectiveOverrideValue(source.overrideMaxBytes, source.maxBytes, source.hostDefaults.maxBytes));
            setSourceControlValue('overrideWindowsCopyFileApi', source.overrideWindowsCopyFileApi);
            setSourceControlValue('useWindowsCopyFileApi', windowsCopyFileEnabled(effectiveOverrideValue(source.overrideWindowsCopyFileApi, source.useWindowsCopyFileApi, source.hostDefaults.useWindowsCopyFileApi)));
            setSourceControlValue('overrideCredentials', source.overrideCredentials);
            setSourceControlValue('username', effectiveOverrideValue(source.overrideCredentials, source.username, source.hostDefaults.username));
            setSourceControlValue('password', source.password);
            setSourceControlValue('overrideClientCertificate', source.overrideClientCertificate);
            populateClientCertificateSelect('Source', source);
            populateSharePointCertificateSelect('Source', source);
            setSourceControlValue('sharePointCertificateSha1', source.sharePointCertificateSha1 || source.clientCertificateSha1);
            setSourceControlValue('sharePointRealmId', source.sharePointRealmId);
            setSourceControlValue('sharePointClientSecret', source.sharePointClientSecret);
            setSourceControlValue('googleProjectId', source.googleProjectId);
            setSourceControlValue('googleOAuthClientId', source.googleOAuthClientId);
            setSourceControlValue('googleOAuthClientSecret', source.googleOAuthClientSecret);
            setSourceControlValue('emailUseOAuth', source.overrideCredentials ? source.emailUseOAuth : source.hostDefaults.emailUseOAuth);
            setSourceControlValue('emailOAuthType', source.overrideCredentials ? source.emailOAuthType : (source.hostDefaults.emailOAuthTenantId ? 'microsoft365' : 'custom'));
            setSourceControlValue('emailOAuthUsername', source.overrideCredentials ? source.emailOAuthUsername : source.hostDefaults.username);
            setSourceControlValue('emailOAuthClientId', source.overrideCredentials ? source.emailOAuthClientId : source.hostDefaults.emailOAuthClientId);
            setSourceControlValue('emailOAuthScope', source.overrideCredentials ? source.emailOAuthScope : source.hostDefaults.emailOAuthScope);
            setSourceControlValue('emailOAuthTokenUrl', source.overrideCredentials ? source.emailOAuthTokenUrl : source.hostDefaults.emailOAuthTokenUrl);
            setSourceControlValue('emailOAuthTenantId', source.overrideCredentials ? source.emailOAuthTenantId : source.hostDefaults.emailOAuthTenantId);
            setSourceControlValue('emailOAuthClientSecret', source.emailOAuthClientSecret);
            setSourceControlValue('overrideClientKey', source.overrideClientKey);
            populateClientKeySelect('Source', source);
            setSourceControlValue('clientKeyId', effectiveOverrideValue(source.overrideClientKey, source.clientKeyId, source.hostDefaults.clientKeyId) || '0');
            setSourceControlValue('overrideFileSorting', source.overrideFileSorting);
            const sourceSort = splitSort(effectiveOverrideValue(source.overrideFileSorting, source.sort, source.hostDefaults.sort));
            setSourceControlValue('sortField', sourceSort.field);
            setSourceControlValue('sortDirection', sourceSort.direction);
            setSourceControlValue('overrideConnectTimeout', source.overrideConnectTimeout);
            setSourceControlValue('connectTimeoutSeconds', effectiveOverrideValue(source.overrideConnectTimeout, source.connectTimeoutSeconds, source.hostDefaults.connectTimeoutSeconds));
            setSourceControlValue('overrideDataTimeout', source.overrideDataTimeout);
            setSourceControlValue('dataTimeoutSeconds', effectiveOverrideValue(source.overrideDataTimeout, source.dataTimeoutSeconds, source.hostDefaults.dataTimeoutSeconds));
            setSourceControlValue('additionalCommandsBefore', source.additionalCommandsBefore);
            setSourceControlValue('additionalCommandsAfter', source.additionalCommandsAfter);
            setSourceControlValue('overrideTransferType', source.overrideTransferType);
            setSourceControlValue('transferType', effectiveOverrideValue(source.overrideTransferType, source.transferType, source.hostDefaults.transferType) || 'binary');
            setSourceControlValue('overridePassive', source.overridePassive);
            setSourceControlValue('passive', Boolean(effectiveOverrideValue(source.overridePassive, source.passive, source.hostDefaults.passive)) ? 1 : 0);
            setSourceControlValue('overrideXSha1', source.overrideXSha1);
            setSourceControlValue('useXSha1', Boolean(effectiveOverrideValue(source.overrideXSha1, source.useXSha1, source.hostDefaults.useXSha1)));
            setSourceControlValue('overrideBlindDownload', source.overrideBlindDownload);
            setSourceControlValue('blindDownload', Boolean(effectiveOverrideValue(source.overrideBlindDownload, source.blindDownload, source.hostDefaults.blindDownload)));
            setSourceControlValue('overrideResume', source.overrideResume);
            setSourceControlValue('resume', Boolean(effectiveOverrideValue(source.overrideResume, source.resume, source.hostDefaults.resume)));
            setSourceControlValue('overrideReuseTlsSession', source.overrideReuseTlsSession);
            setSourceControlValue('reuseTlsSession', Boolean(effectiveOverrideValue(source.overrideReuseTlsSession, source.reuseTlsSession, source.hostDefaults.reuseTlsSession)));
            setSourceControlValue('overrideMd5Mode', source.overrideMd5Mode);
            setSourceControlValue('md5Mode', effectiveOverrideValue(source.overrideMd5Mode, source.md5Mode, source.hostDefaults.md5Mode) || 'Never');
            setSourceControlValue('overrideMd5FileName', source.overrideMd5FileName);
            setSourceControlValue('md5FileName', effectiveOverrideValue(source.overrideMd5FileName, source.md5FileName, source.hostDefaults.md5FileName) || 'MD5SUM');
            setSourceControlValue('overrideDataIntegrity', source.overrideDataIntegrity);
            setSourceControlValue('dataIntegrityRule', effectiveOverrideValue(source.overrideDataIntegrity, source.dataIntegrityRule, source.hostDefaults.dataIntegrityRule) || 'never');
            setSourceControlValue('account', source.account);

            refreshSourceDependencies(source);
        }

        function populateSourceHostSelect(source) {
            if (!sourceHostSelect) return;
            const step = activeStep();
            const selectedType = populateHostTypeFilter('Source', source, step?.key || '');
            sourceHostSelect.replaceChildren(new Option(selectedType ? 'Select a Host' : 'Select a Host Type first', ''));
            sourceHostSelect.options[0].disabled = Boolean(source.hostId);
            filteredHosts('Source', selectedType).forEach(host => {
                const option = new Option(host.name, host.id);
                option.dataset.hostTypeCode = host.typeCode;
                option.disabled = source.serverHostTypeCode
                    ? !canChangeHost('Source', source.serverHostTypeCode, host.typeCode)
                    : !canWriteHostStep('Source', host.typeCode);
                sourceHostSelect.appendChild(option);
            });
            if (source.hostId && !Array.from(sourceHostSelect.options).some(option => option.value === source.hostId)) {
                sourceHostSelect.appendChild(new Option(source.hostName || `Host ID ${source.hostId}`, source.hostId));
            }
            sourceHostSelect.disabled = !selectedType;
            sourceHostSelect.value = source.hostId || '';
            updateSourceHostPresentation(source);
            updateEditHostButtons();
        }

        function selectedHostModel(kind) {
            const step = activeStep();
            return step?.kind === kind ? (kind === 'Source' ? step.source : step.destination) : null;
        }

        function updateEditHostButtons() {
            editHostButtons.forEach(button => {
                const model = selectedHostModel(button.dataset.traditionalTaskEditorEditHost);
                button.hidden = !isMoveItAdmin;
                button.disabled = editingHost || saving || loadingShell || !model?.hostId;
                button.title = 'Edit the selected Host.';
            });
        }

        async function refreshEditedHost(hostId, taskId) {
            const url = new URL(endpoint('traditionalTaskEditorShellUrl'), window.location.href);
            url.searchParams.set('id', taskId);
            const fresh = await fetchJson(url.toString(), { cache: 'no-store' });
            if (activeTaskId !== taskId || !dialog.open) return;
            if (String(fresh?.task?.id || '') !== taskId || !Array.isArray(fresh.sourceHosts) || !Array.isArray(fresh.destinationHosts))
                throw new Error('The refreshed Host information could not be confirmed.');
            const sources = fresh.sourceHosts.map(normalizeHostOption), destinations = fresh.destinationHosts.map(normalizeHostOption);
            const updates = [];
            for (const step of taskSteps) for (const [field, options] of [['source',sources],['destination',destinations]]) {
                const model = step[field];
                if (!model || model.hostId !== hostId) continue;
                const host = options.find(item=>item.id===hostId && item.typeCode===model.hostTypeCode);
                if (!host) throw new Error('The selected Host is unavailable or its type changed. Reopen the editor to inspect it.');
                updates.push({step,model,host});
            }
            // Refresh only Host-derived presentation. Never replace the Task draft, its
            // server revision, explicit Step overrides, or an existing save baseline.
            sourceHosts = sources; destinationHosts = destinations;
            if (Array.isArray(fresh.smtpHosts)) smtpHosts = fresh.smtpHosts.map(normalizeNamedOption);
            for (const {step,model,host} of updates) {
                model.hostName = host.name; model.hostType = host.type;
                model.hostDefaults = normalizeHostDefaults(host.defaults);
                step.subtitle = stepSummaryText(step);
            }
            renderStepRail();
            const step = activeStep();
            if (step?.source) renderSource(step);
            else if (step?.destination) renderDestination(step);
            if (isAdvancedTask()) advancedEditor?.refresh();
        }

        async function editSelectedHost(kind, button) {
            const model = selectedHostModel(kind);
            if (!isMoveItAdmin || editingHost || saving || loadingShell || !model?.hostId) return false;
            const owner = activeTaskId, hostId = model.hostId;
            let finished = false;
            const returned = async (refresh = true) => {
                if (finished || activeTaskId !== owner) return;
                finished = true;
                try {
                    if (refresh && dialog.open) {
                        setFooterStatus('Refreshing Host information…');
                        await refreshEditedHost(hostId, owner);
                    }
                } catch (error) {
                    footerSaveError = `Unable to refresh Host information. Your Task draft was kept. ${error?.message || ''}`;
                    autoSaveBlockedSignature = editorStateSignature();
                } finally {
                    if (activeTaskId === owner) {
                        editingHost = false;
                        updateDirtyState();
                        if (dialog.open) button?.focus();
                    }
                }
            };
            editingHost = true;
            window.clearTimeout(autoSaveTimer); autoSaveTimer = 0;
            updateDirtyState();
            try {
                if (!window.MauiEditorRouting?.openHost) throw new Error('The Host editor is unavailable. Reload MAUI and try again.');
                const opened = await window.MauiEditorRouting.openHost(hostId, model.hostName || '', { inPlace: true, onClosed: () => returned() });
                if (!opened && !document.querySelector('[data-host-editor-dialog][open]')) await returned(false);
                return opened;
            } catch (error) {
                await returned(false);
                setFooterStatus(error?.message || 'Unable to open the Host editor.', 'error');
                return false;
            }
        }

        function updateSourceHostPresentation(source) {
            const host = sourceHosts.find(item => item.id === source.hostId);
            if (sourceHostHelp) {
                const parts = [host?.type || source.hostType, host?.destination].filter(Boolean);
                if (source.hostId && !canWriteHostStep('Source', source.hostTypeCode)) parts.push('Read-only in MAUI until this Host type has a verified write profile');
                sourceHostHelp.textContent = parts.join(' · ') || 'Select the MOVEit Host used by this Source.';
            }
            if (sourceHostType) sourceHostType.textContent = `${source.hostType || host?.type || 'Host'} Overrides`;
        }

        function containsMoveItMacro(value) {
            return /\[[^\]\r\n]+\]/.test(String(value || ''));
        }

        function updateFolderBrowseAvailability(kind, model, button, warning) {
            if (!button || !model) return;
            const hasMacro = containsMoveItMacro(model.path);
            const browseProfile = hostBrowseProfile(kind, model.hostTypeCode);
            const blindDownloadBlocksBrowse = kind === 'Source'
                && [HOST_WRITE_PROFILE_FTP, HOST_WRITE_PROFILE_SFTP].includes(browseProfile)
                && Boolean(effectiveOverrideValue(model.overrideBlindDownload, model.blindDownload, model.hostDefaults?.blindDownload));
            const browsable = Boolean(model.hostId) && canBrowseHostStep(kind, model.hostTypeCode) && Boolean(hostBrowseProfile(kind, model.hostTypeCode));
            if (warning) warning.hidden = !hasMacro;
            button.disabled = !browsable || hasMacro || blindDownloadBlocksBrowse;
            if (hasMacro) {
                button.title = 'Folders containing macros cannot be browsed. Remove the macro to use Browse.';
            } else if (blindDownloadBlocksBrowse) {
                button.title = `${model.hostType || hostTypeLabel(model.hostTypeCode)} Blind Downloads do not use directory listings, so this Source cannot be browsed.`;
            } else if (browsable) {
                button.title = `Browse folders on this ${model.hostType || hostTypeLabel(model.hostTypeCode)} Host.`;
            } else {
                button.title = `Folder browsing is not enabled for ${model.hostType || hostTypeLabel(model.hostTypeCode)} ${kind} steps in this build.`;
            }
        }

        const SPECIAL_FILTER_SIZE_OPERATORS = [
            ['eq', '='], ['ne', '!='], ['gt', '>'], ['ge', '>='], ['lt', '<'], ['le', '<=']
        ];
        const SPECIAL_FILTER_DATE_OPERATORS = [['newer', 'newer than'], ['older', 'older than']];
        const SPECIAL_FILTER_SIZE_UNITS = ['B', 'KB', 'MB', 'GB'];
        const SPECIAL_FILTER_DATE_UNITS = [
            ['hours', 'Hour(s) Ago'], ['days', 'Day(s) Ago'], ['months', 'Month(s) Ago'], ['years', 'Year(s) Ago']
        ];

        function defaultSpecialFilterCriterion() {
            return { type: 'fileSize', operator: 'eq', value: '0', unit: 'B' };
        }

        function specialFilterOperatorLabel(filter) {
            const options = filter.type === 'fileModified' ? SPECIAL_FILTER_DATE_OPERATORS : SPECIAL_FILTER_SIZE_OPERATORS;
            return options.find(item => item[0] === filter.operator)?.[1] || filter.operator;
        }

        function specialFilterUnitLabel(filter) {
            if (filter.type !== 'fileModified') return filter.unit || 'B';
            return SPECIAL_FILTER_DATE_UNITS.find(item => item[0] === filter.unit)?.[1]
                ?.replace('(s)', Number(filter.value) === 1 ? '' : 's') || filter.unit;
        }

        function specialFilterCriterionSummary(filter) {
            if (filter.type === 'fileModified')
                return `File Modified Timestamp ${specialFilterOperatorLabel(filter)} ${filter.value} ${specialFilterUnitLabel(filter).toLowerCase()}`;
            return `File Size ${specialFilterOperatorLabel(filter)} ${filter.value} ${filter.unit || 'B'}`;
        }

        function specialFilterSummary(source) {
            const model = source?.specialFilters;
            if (model?.hasUnsupportedCriteria)
                return 'Existing Special Filter criteria include a pattern MAUI does not recognize; they will be preserved unchanged.';
            const filters = Array.isArray(model?.filters) ? model.filters : [];
            if (!filters.length) return 'No filters configured.';
            const joiner = String(model.matchMode || 'all').toLowerCase() === 'any' ? ' OR ' : ' AND ';
            const summary = filters.map(specialFilterCriterionSummary).join(joiner);
            return source?.hasSpecialFilters ? summary : `Disabled — ${summary}`;
        }

        function specialFilterSizeToBytes(value, unit) {
            const numeric = Number(value);
            if (!Number.isFinite(numeric) || numeric < 0) return null;
            const multiplier = ({ B: 1, KB: 1024, MB: 1024 * 1024, GB: 1024 * 1024 * 1024 })[String(unit || 'B').toUpperCase()];
            if (!multiplier) return null;
            const bytes = numeric * multiplier;
            return Number.isSafeInteger(bytes) ? bytes : null;
        }

        function buildSourceSpecialFilterCriteria(source) {
            const model = source?.specialFilters;
            if (model?.hasUnsupportedCriteria) return undefined; // Preserve the fresh server Criteria unchanged.
            if (!source?.hasSpecialFilters) return null;
            const filters = Array.isArray(model?.filters) ? model.filters : [];
            const comp = filters.map(filter => {
                if (filter.type === 'fileModified') {
                    const unitCode = ({ hours: 'H', days: 'D', months: 'M', years: 'Y' })[filter.unit];
                    return {
                        a: '[FileDateStamp]',
                        test: filter.operator === 'older' ? 'DLT' : 'DGT',
                        b: `[DateSubtract([Now],${Number(filter.value)}${unitCode})]`
                    };
                }
                const test = ({ eq: 'NEQ', ne: 'NNE', gt: 'NGT', ge: 'NGE', lt: 'NLT', le: 'NLE' })[filter.operator] || 'NEQ';
                return { a: '[FileSize]', test, b: String(specialFilterSizeToBytes(filter.value, filter.unit)) };
            });
            // MOVEit's schema requires And/Or groups to contain at least two comparisons.
            // A single Special Filter is represented by Criteria.comp directly.
            if (comp.length === 1) return { comp: comp[0] };
            return { [String(model?.matchMode || 'all').toLowerCase() === 'any' ? 'Or' : 'And']: { comp } };
        }

        function renderSpecialFilterRows() {
            if (!specialFilterRows || !specialFilterDraft) return;
            specialFilterRows.replaceChildren();
            const readOnly = Boolean(specialFilterDraft.hasUnsupportedCriteria);
            specialFilterDraft.filters.forEach((filter, index) => {
                const row = document.createElement('div');
                row.className = 'traditional-task-editor-special-filter-row';

                const type = document.createElement('select');
                type.className = 'text-input';
                type.append(new Option('File Size', 'fileSize'), new Option('File Modified Timestamp', 'fileModified'));
                type.value = filter.type === 'fileModified' ? 'fileModified' : 'fileSize';
                type.disabled = readOnly;

                const operator = document.createElement('select');
                operator.className = 'text-input';
                const operatorOptions = type.value === 'fileModified' ? SPECIAL_FILTER_DATE_OPERATORS : SPECIAL_FILTER_SIZE_OPERATORS;
                operatorOptions.forEach(item => operator.appendChild(new Option(item[1], item[0])));
                operator.value = operatorOptions.some(item => item[0] === filter.operator) ? filter.operator : operatorOptions[0][0];
                operator.disabled = readOnly;

                const value = document.createElement('input');
                value.type = 'number';
                value.min = '0';
                value.step = type.value === 'fileModified' ? '1' : 'any';
                value.className = 'text-input';
                value.value = String(filter.value ?? '');
                value.disabled = readOnly;

                const unit = document.createElement('select');
                unit.className = 'text-input';
                if (type.value === 'fileModified') {
                    SPECIAL_FILTER_DATE_UNITS.forEach(item => unit.appendChild(new Option(item[1], item[0])));
                    unit.value = SPECIAL_FILTER_DATE_UNITS.some(item => item[0] === filter.unit) ? filter.unit : 'hours';
                } else {
                    SPECIAL_FILTER_SIZE_UNITS.forEach(item => unit.appendChild(new Option(item, item)));
                    unit.value = SPECIAL_FILTER_SIZE_UNITS.includes(String(filter.unit || '').toUpperCase()) ? String(filter.unit).toUpperCase() : 'B';
                }
                unit.disabled = readOnly;

                const remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'secondary-button compact-button danger-text-button traditional-task-editor-special-filter-remove';
                remove.textContent = 'Remove';
                remove.disabled = readOnly;
                remove.addEventListener('click', () => {
                    if (readOnly) return;
                    specialFilterDraft.filters.splice(index, 1);
                    renderSpecialFilterRows();
                });

                type.addEventListener('change', () => {
                    filter.type = type.value;
                    filter.operator = type.value === 'fileModified' ? 'newer' : 'eq';
                    filter.unit = type.value === 'fileModified' ? 'hours' : 'B';
                    if (!String(filter.value || '').trim()) filter.value = type.value === 'fileModified' ? '1' : '0';
                    renderSpecialFilterRows();
                });
                operator.addEventListener('change', () => { filter.operator = operator.value; });
                value.addEventListener('input', () => { filter.value = value.value; });
                unit.addEventListener('change', () => { filter.unit = unit.value; });

                row.append(type, operator, value, unit, remove);
                specialFilterRows.appendChild(row);
            });

            specialFilterMatchControls.forEach(control => {
                control.checked = control.value === specialFilterDraft.matchMode;
                control.disabled = readOnly;
            });
            if (specialFilterMore) specialFilterMore.disabled = readOnly || specialFilterDraft.filters.length >= 6;
            if (specialFiltersReadonly) specialFiltersReadonly.hidden = !readOnly;
            if (specialFiltersApply) specialFiltersApply.textContent = readOnly ? 'Close' : 'Apply';
        }

        function showSpecialFiltersEditor() {
            const step = activeStep();
            const source = step?.source;
            if (!source || !specialFiltersDialog) return;
            const existing = source.specialFilters ? deepClone(source.specialFilters) : null;
            specialFilterDraft = existing || { matchMode: 'all', hasUnsupportedCriteria: false, filters: [defaultSpecialFilterCriterion()] };
            if (!specialFilterDraft.hasUnsupportedCriteria && !specialFilterDraft.filters.length)
                specialFilterDraft.filters = [defaultSpecialFilterCriterion()];
            if (specialFiltersSubtitle) specialFiltersSubtitle.textContent = source.hostName || source.path || 'Source Special Filters';
            if (specialFiltersError) { specialFiltersError.hidden = true; specialFiltersError.textContent = ''; }
            renderSpecialFilterRows();
            specialFiltersDialog.showModal();
        }

        function closeSpecialFiltersEditor() {
            if (specialFiltersDialog?.open) specialFiltersDialog.close();
            specialFilterDraft = null;
        }

        function validateSpecialFilterDraft() {
            if (!specialFilterDraft || specialFilterDraft.hasUnsupportedCriteria) return '';
            if (!specialFilterDraft.filters.length) return 'At least one Special Filter is required.';
            if (specialFilterDraft.filters.length > 6) return 'MOVEit supports up to six Source Special Filters.';
            for (const filter of specialFilterDraft.filters) {
                if (filter.type === 'fileModified') {
                    if (!/^\d+$/.test(String(filter.value || '')) || Number(filter.value) < 0) return 'File Modified Timestamp values must be whole numbers greater than or equal to zero.';
                    if (!SPECIAL_FILTER_DATE_UNITS.some(item => item[0] === filter.unit)) return 'Select a valid File Modified Timestamp unit.';
                    if (!SPECIAL_FILTER_DATE_OPERATORS.some(item => item[0] === filter.operator)) return 'Select a valid File Modified Timestamp condition.';
                } else {
                    if (specialFilterSizeToBytes(filter.value, filter.unit) == null) return 'File Size values must resolve to a non-negative whole number of bytes.';
                    if (!SPECIAL_FILTER_SIZE_OPERATORS.some(item => item[0] === filter.operator)) return 'Select a valid File Size condition.';
                }
            }
            return '';
        }

        function applySpecialFiltersEditor() {
            const step = activeStep();
            const source = step?.source;
            if (!source || !specialFilterDraft) return;
            if (specialFilterDraft.hasUnsupportedCriteria) { closeSpecialFiltersEditor(); return; }
            const validation = validateSpecialFilterDraft();
            if (validation) {
                if (specialFiltersError) { specialFiltersError.textContent = validation; specialFiltersError.hidden = false; }
                return;
            }
            const before = deepClone(source);
            source.specialFilters = deepClone(specialFilterDraft);
            source.hasSpecialFilters = true;
            lastUndo = { kind: 'source-snapshot', stepKey: step.key, before, label: 'Special Filters' };
            seedSourceStableValues(step);
            updateUndoState();
            closeSpecialFiltersEditor();
            renderSource(step);
            updateDirtyState();
        }

        function refreshSourceDependencies(source) {
            const ignoreFolders = dialog.querySelector('[data-source-field="ignoreFolders"]');
            const excludeFolders = dialog.querySelector('[data-source-field="excludeFolders"]');
            const ignoreFiles = dialog.querySelector('[data-source-field="ignoreFiles"]');
            const excludeFiles = dialog.querySelector('[data-source-field="excludeFiles"]');
            if (excludeFolders) excludeFolders.disabled = !(ignoreFolders?.checked);
            if (excludeFiles) excludeFiles.disabled = !(ignoreFiles?.checked);

            const renameSelected = Number(source.deleteOriginalAction) === 2;
            if (renameOptions) renameOptions.classList.toggle('disabled', !renameSelected);
            if (renameOptions) renameOptions.querySelectorAll('input').forEach(control => { control.disabled = !renameSelected; });

            const special = dialog.querySelector('[data-source-field="hasSpecialFilters"]');
            const specialFiltersUnsupported = Boolean(source.specialFilters?.hasUnsupportedCriteria);
            if (special) special.disabled = specialFiltersUnsupported;
            if (specialFiltersButton) specialFiltersButton.disabled = false;
            if (specialFilterStatus) specialFilterStatus.textContent = specialFilterSummary(source);

            setOverrideFieldState('overrideRetryCount', 'retryCount', source.hostDefaults.retryCount);
            setOverrideFieldState('overrideRetryTimeout', 'retryTimeoutSeconds', source.hostDefaults.retryTimeoutSeconds);
            setOverrideFieldState('overrideRescanTime', 'rescanSeconds', source.hostDefaults.rescanSeconds);
            setOverrideFieldState('overrideMaxFiles', 'maxFiles', source.hostDefaults.maxFiles);
            setOverrideFieldState('overrideMaxBytes', 'maxBytes', source.hostDefaults.maxBytes);
            setOverrideFieldState('overrideWindowsCopyFileApi', 'useWindowsCopyFileApi', windowsCopyFileEnabled(source.hostDefaults.useWindowsCopyFileApi));
            setOverrideFieldState('overrideConnectTimeout', 'connectTimeoutSeconds', source.hostDefaults.connectTimeoutSeconds);
            setOverrideFieldState('overrideDataTimeout', 'dataTimeoutSeconds', source.hostDefaults.dataTimeoutSeconds);
            setSourceProtocolOverrideState('overrideTransferType', 'transferType', source.hostDefaults.transferType);
            setSourceProtocolOverrideState('overridePassive', 'passive', source.hostDefaults.passive);
            setSourceProtocolOverrideState('overrideXSha1', 'useXSha1', source.hostDefaults.useXSha1);
            setSourceProtocolOverrideState('overrideBlindDownload', 'blindDownload', source.hostDefaults.blindDownload);
            setSourceProtocolOverrideState('overrideResume', 'resume', source.hostDefaults.resume);
            setSourceProtocolOverrideState('overrideReuseTlsSession', 'reuseTlsSession', source.hostDefaults.reuseTlsSession);
            setSourceProtocolOverrideState('overrideMd5Mode', 'md5Mode', source.hostDefaults.md5Mode);
            setSourceProtocolOverrideState('overrideMd5FileName', 'md5FileName', source.hostDefaults.md5FileName);
            setSourceProtocolOverrideState('overrideDataIntegrity', 'dataIntegrityRule', source.hostDefaults.dataIntegrityRule);
            setDependentControls('Source', ['username', 'password', 'account'], source.overrideCredentials);
            setDependentControls('Source', ['clientCertificateSha1'], source.overrideClientCertificate);
            setDependentControls('Source', ['clientKeyId'], source.overrideClientKey);
            setDependentControls('Source', ['sortField', 'sortDirection'], source.overrideFileSorting);
            setDependentControls('Source', ['sharePointAuthenticationMode', 'sharePointClientId', 'sharePointTenantId', 'sharePointRealmId', 'sharePointClientSecret', 'sharePointCertificateSha1', 'googleUseOAuth', 'googleProjectId', 'googleOAuthClientId', 'googleOAuthClientSecret', 'emailUseOAuth', 'emailOAuthType', 'emailOAuthUsername', 'emailOAuthClientId', 'emailOAuthClientSecret', 'emailOAuthTenantId', 'emailOAuthScope', 'emailOAuthTokenUrl'], source.overrideCredentials);
            const sourceProfile = hostWriteProfile('Source', source.hostTypeCode);
            const sourceIsS3 = sourceProfile === HOST_WRITE_PROFILE_S3;
            const sourceIsAzureBlob = sourceProfile === HOST_WRITE_PROFILE_AZURE_BLOB;
            const sourceIsSharePoint = sourceProfile === HOST_WRITE_PROFILE_SHAREPOINT;
            const sourceIsGoogleStorage = sourceProfile === HOST_WRITE_PROFILE_GOOGLE_STORAGE;
            const sourceIsPop3 = sourceProfile === HOST_WRITE_PROFILE_POP3;
            const sourceEffectiveEmailOAuth = sourceIsPop3 && (source.overrideCredentials ? Boolean(source.emailUseOAuth) : source.hostDefaults.emailUseOAuth === true);
            const sourceEffectiveEmailOAuthType = source.overrideCredentials
                ? (source.emailOAuthType || (source.emailOAuthTenantId ? 'microsoft365' : 'custom'))
                : (source.hostDefaults.emailOAuthTenantId ? 'microsoft365' : 'custom');
            const sourceEffectiveGoogleOAuth = source.overrideCredentials ? Boolean(source.googleUseOAuth) : source.hostDefaults.googleUseOAuth === true;
            const sourceEffectiveSharePointMode = Number(source.overrideCredentials ? (source.sharePointAuthenticationMode ?? 2) : (source.hostDefaults.sharePointAuthenticationMode ?? 2));
            if (sourceCredentialUserLabel) sourceCredentialUserLabel.textContent = sourceIsS3 || (sourceIsGoogleStorage && !sourceEffectiveGoogleOAuth) ? 'Access Key ID' : sourceIsAzureBlob ? 'Storage Account' : 'Username';
            if (sourceCredentialSecretLabel) sourceCredentialSecretLabel.textContent = sourceIsS3 || (sourceIsGoogleStorage && !sourceEffectiveGoogleOAuth) ? 'Secret Access Key' : sourceIsAzureBlob ? 'Key' : 'Password';
            const sourceGoogleHmac = dialog.querySelector('[data-traditional-task-editor-source-google-hmac]');
            const sourceGoogleOAuth = dialog.querySelector('[data-traditional-task-editor-source-google-oauth]');
            const sourceGoogleAuthHelp = dialog.querySelector('[data-traditional-task-editor-source-google-auth-help]');
            if (sourceGoogleHmac) sourceGoogleHmac.checked = sourceIsGoogleStorage && !sourceEffectiveGoogleOAuth;
            if (sourceGoogleOAuth) sourceGoogleOAuth.checked = sourceIsGoogleStorage && sourceEffectiveGoogleOAuth;
            const sourceGenericCredentials = dialog.querySelector('[data-source-generic-credentials]');
            if (sourceGenericCredentials) sourceGenericCredentials.hidden = (sourceIsGoogleStorage && sourceEffectiveGoogleOAuth) || sourceIsSharePoint || sourceEffectiveEmailOAuth;
            const sourceGoogleOAuthFields = dialog.querySelector('[data-source-google-oauth-fields]');
            if (sourceGoogleOAuthFields) sourceGoogleOAuthFields.hidden = !(sourceIsGoogleStorage && sourceEffectiveGoogleOAuth);
            const sourceSharePointClientIdFields = dialog.querySelector('[data-source-sharepoint-client-id-fields]');
            const sourceSharePointAppFields = dialog.querySelector('[data-source-sharepoint-app-fields]');
            const sourceSharePointCertificateFields = dialog.querySelector('[data-source-sharepoint-certificate-fields]');
            if (sourceSharePointClientIdFields) sourceSharePointClientIdFields.hidden = !(sourceIsSharePoint && [1, 2].includes(sourceEffectiveSharePointMode));
            if (sourceSharePointAppFields) sourceSharePointAppFields.hidden = !(sourceIsSharePoint && sourceEffectiveSharePointMode === 1);
            if (sourceSharePointCertificateFields) sourceSharePointCertificateFields.hidden = !(sourceIsSharePoint && sourceEffectiveSharePointMode === 2);
            const sourceGoogleSecretHelp = dialog.querySelector('[data-traditional-task-editor-source-google-oauth-secret-help]');
            if (sourceGoogleSecretHelp) sourceGoogleSecretHelp.textContent = source.googleOAuthClientSecretConfigured && !source.googleOAuthClientSecret ? 'A Step private key is configured. Leave blank to keep it, or enter a replacement.' : 'Enter the Google OAuth private key for this Step override.';
            const sourceSharePointSecretHelp = dialog.querySelector('[data-traditional-task-editor-source-sharepoint-client-secret-help]');
            if (sourceSharePointSecretHelp) sourceSharePointSecretHelp.textContent = source.sharePointClientSecretConfigured && !source.sharePointClientSecret ? 'A Step client secret is configured. Leave blank to keep it, or enter a replacement.' : 'Enter the SharePoint App Access client secret for this Step override.';
            if (sourceGoogleAuthHelp && sourceIsGoogleStorage) sourceGoogleAuthHelp.textContent = source.overrideCredentials
                ? (sourceEffectiveGoogleOAuth ? 'Using a Task-level OAuth 2.0 override.' : 'Using a Task-level HMAC override.')
                : `Authentication is inherited from the selected Host (${sourceEffectiveGoogleOAuth ? 'OAuth 2.0' : 'HMAC'}).`;
            const sourceEmailLegacy = dialog.querySelector('[data-traditional-task-editor-source-email-legacy]');
            const sourceEmailOAuth = dialog.querySelector('[data-traditional-task-editor-source-email-oauth]');
            const sourceEmailOAuthDetails = dialog.querySelector('[data-traditional-task-editor-source-email-oauth-details]');
            const sourceEmailAuthHelp = dialog.querySelector('[data-traditional-task-editor-source-email-auth-help]');
            const sourceEmailOAuthSecretHelp = dialog.querySelector('[data-traditional-task-editor-source-email-oauth-secret-help]');
            const sourceOverrideCredentialsControl = dialog.querySelector('[data-source-field="overrideCredentials"]');
            if (sourceEmailLegacy) sourceEmailLegacy.checked = sourceIsPop3 && !sourceEffectiveEmailOAuth;
            if (sourceEmailOAuth) sourceEmailOAuth.checked = sourceIsPop3 && sourceEffectiveEmailOAuth;
            if (sourceEmailOAuthDetails) sourceEmailOAuthDetails.hidden = !(sourceIsPop3 && sourceEffectiveEmailOAuth);
            dialog.querySelectorAll('[data-source-email-oauth-microsoft-only]').forEach(item => { item.hidden = !(sourceIsPop3 && sourceEffectiveEmailOAuth && sourceEffectiveEmailOAuthType === 'microsoft365'); });
            dialog.querySelectorAll('[data-source-email-oauth-custom-only]').forEach(item => { item.hidden = !(sourceIsPop3 && sourceEffectiveEmailOAuth && sourceEffectiveEmailOAuthType === 'custom'); });
            if (sourceOverrideCredentialsControl && sourceIsPop3) {
                sourceOverrideCredentialsControl.disabled = false;
                sourceOverrideCredentialsControl.title = '';
            }
            if (sourceEmailOAuthSecretHelp && sourceIsPop3 && sourceEffectiveEmailOAuth) {
                sourceEmailOAuthSecretHelp.textContent = source.overrideCredentials
                    ? (source.emailOAuthClientSecretConfigured && !source.emailOAuthClientSecret ? 'A Step client secret is configured. Leave blank to keep it, or enter a replacement.' : 'Enter the OAuth client secret for this Step override.')
                    : 'Using the OAuth client secret configured on the POP3 Host.';
            }
            if (sourceEmailAuthHelp && sourceIsPop3) sourceEmailAuthHelp.textContent = source.overrideCredentials
                ? (sourceEffectiveEmailOAuth ? `Using a Task-level OAuth override (${sourceEffectiveEmailOAuthType === 'microsoft365' ? 'Microsoft Office 365' : 'Custom'}).` : 'Using a Task-level Legacy username/password override.')
                : `Authentication is inherited from the selected POP3 Host (${sourceEffectiveEmailOAuth ? 'OAuth' : 'Legacy'}).`;
            if (sourceBucketHelp) sourceBucketHelp.textContent = source.useDefaultBucket
                ? (source.hostDefaults.bucket ? `Using Host default bucket: ${source.hostDefaults.bucket}` : 'Using the Host default bucket setting.')
                : 'This Bucket Name overrides the Host default for this Source.';
            if (sourceContainerHelp) sourceContainerHelp.textContent = source.useDefaultContainer
                ? (source.hostDefaults.container ? `Using Host default container: ${source.hostDefaults.container}` : 'Using the Host default container setting.')
                : 'This Container overrides the Host default for this Source.';
            if (sourceDocumentLibraryHelp) sourceDocumentLibraryHelp.textContent = source.useDefaultDocumentLibrary
                ? (source.hostDefaults.documentLibrary ? `Using Host default library: ${source.hostDefaults.documentLibrary}` : 'Using the Host default document library setting.')
                : 'This Document Library overrides the Host default for this Source.';
            if (sourcePasswordHelp) sourcePasswordHelp.textContent = source.overrideCredentials
                ? (source.passwordConfigured && !source.password ? 'A Step password is configured. Leave blank to keep it, or enter a replacement.' : 'Enter the password for this Step override.')
                : 'Using the Host default credentials.';
            if (sourceClientCertHelp) {
                const selected = tlsCertificates.find(item => item.sha1Thumbprint.replaceAll(' ', '').toLowerCase() === String(source.clientCertificateSha1 || '').replaceAll(' ', '').toLowerCase());
                sourceClientCertHelp.textContent = source.overrideClientCertificate
                    ? (selected ? `${selected.name}${selected.expirationDate ? ` · expires ${selected.expirationDate}` : ''}` : (source.clientCertificateSubject || 'No client certificate selected.'))
                    : (source.hostDefaults.clientCertificateSubject ? `Using Host default: ${source.hostDefaults.clientCertificateSubject}` : 'Using the Host default client certificate setting.');
            }
            if (sourceClientKeyHelp) {
                const effectiveKeyId = String(effectiveOverrideValue(source.overrideClientKey, source.clientKeyId, source.hostDefaults.clientKeyId) || '0');
                const selectedKey = sshClientKeys.find(item => String(item.id) === effectiveKeyId);
                sourceClientKeyHelp.textContent = source.overrideClientKey
                    ? (selectedKey ? selectedKey.name : (effectiveKeyId === '0' ? 'No client key will be used for this Source override.' : `SSH Client Key ID ${effectiveKeyId}`))
                    : (selectedKey ? `Using Host default: ${selectedKey.name}` : (effectiveKeyId === '0' ? 'Using the Host default client key setting.' : `Using Host default SSH Client Key ID ${effectiveKeyId}`));
            }
            const sourceAccountHelp = dialog.querySelector('[data-traditional-task-editor-source-account-help]');
            if (sourceAccountHelp) sourceAccountHelp.textContent = source.overrideCredentials
                ? (source.accountConfigured && !source.account ? 'A Step account is configured. Leave blank to keep it, or enter a replacement.' : 'Optional; only FTP servers that require an account value use this setting.')
                : 'Using the Host default account setting.';
            const effectiveResume = Boolean(effectiveOverrideValue(source.overrideResume, source.resume, source.hostDefaults.resume));
            const effectiveBlindDownload = Boolean(effectiveOverrideValue(source.overrideBlindDownload, source.blindDownload, source.hostDefaults.blindDownload));
            const effectiveRetryCount = Number(effectiveOverrideValue(source.overrideRetryCount, source.retryCount, source.hostDefaults.retryCount) ?? 0);
            const resumeRetryWarnings = dialog.querySelectorAll('[data-traditional-task-editor-source-ftp-resume-retry-warning], [data-traditional-task-editor-source-sftp-resume-retry-warning]');
            const resumeBlindWarnings = dialog.querySelectorAll('[data-traditional-task-editor-source-ftp-resume-blind-warning], [data-traditional-task-editor-source-sftp-resume-blind-warning]');
            resumeRetryWarnings.forEach(item => { item.hidden = !(effectiveResume && effectiveRetryCount === 0); });
            resumeBlindWarnings.forEach(item => { item.hidden = !(effectiveResume && effectiveBlindDownload); });

            const sourceRenameOriginalOption = dialog.querySelector('[data-source-rename-original-option]');
            if (sourceRenameOriginalOption) {
                const cloudRenameUnavailable = [HOST_WRITE_PROFILE_S3, HOST_WRITE_PROFILE_AZURE_BLOB, HOST_WRITE_PROFILE_SHAREPOINT, HOST_WRITE_PROFILE_GOOGLE_STORAGE, HOST_WRITE_PROFILE_POP3].includes(sourceProfile);
                sourceRenameOriginalOption.hidden = cloudRenameUnavailable && Number(source.deleteOriginalAction || 0) !== 2;
            }

            configureHostOverrideTabs(source);
            updateSourceHostPresentation(source);
            updateFolderBrowseAvailability('Source', source, sourceBrowseButton, sourceBrowseWarning);
            const step = activeStep();
            if (step) configureStepActions(step);
            applyHostStepWriteAvailability('Source', source);
            applySyncSourcePresentation(source);
        }

        function setOverrideFieldState(toggleField, valueField, defaultValue) {
            const toggle = dialog.querySelector(`[data-source-field="${toggleField}"]`);
            const value = dialog.querySelector(`[data-source-field="${valueField}"]`);
            if (!value) return;
            const override = Boolean(toggle?.checked);
            value.disabled = !override;
            value.title = override ? '' : defaultValue == null ? 'Using the Host default.' : `Using Host default: ${formatValue(defaultValue)}`;
        }

        function setSourceProtocolOverrideState(toggleField, valueField, defaultValue) {
            const toggle = dialog.querySelector(`[data-source-field="${toggleField}"]`);
            const override = Boolean(toggle?.checked);
            dialog.querySelectorAll(`[data-source-field="${valueField}"]`).forEach(value => {
                value.disabled = !override;
                value.title = override ? '' : defaultValue == null ? 'Using the Host default.' : `Using Host default: ${formatValue(defaultValue)}`;
            });
        }

        function configureHostOverrideTabs(source) {
            const typeCode = String(source.hostTypeCode || '').trim();
            const writeProfile = hostWriteProfile('Source', typeCode);
            const isUnc = writeProfile === HOST_WRITE_PROFILE_SHARE;
            const isLocal = writeProfile === HOST_WRITE_PROFILE_LOCAL;
            const isMoveItTransfer = writeProfile === HOST_WRITE_PROFILE_MOVEIT_TRANSFER;
            const isFtp = writeProfile === HOST_WRITE_PROFILE_FTP;
            const isSftp = writeProfile === HOST_WRITE_PROFILE_SFTP;
            const isS3 = writeProfile === HOST_WRITE_PROFILE_S3;
            const isAzureBlob = writeProfile === HOST_WRITE_PROFILE_AZURE_BLOB;
            const isSharePoint = writeProfile === HOST_WRITE_PROFILE_SHAREPOINT;
            const isGoogleStorage = writeProfile === HOST_WRITE_PROFILE_GOOGLE_STORAGE;
            const isPop3 = writeProfile === HOST_WRITE_PROFILE_POP3;
            const supported = canWriteHostStep('Source', typeCode);

            if (hostOverrideSupported) hostOverrideSupported.hidden = !supported;
            if (hostOverrideUnsupported) {
                hostOverrideUnsupported.hidden = supported;
                if (!supported) {
                    hostOverrideUnsupported.textContent = typeCode
                        ? `${source.hostType || typeCode} Source overrides do not yet have a verified MAUI write profile. Existing task values are preserved.`
                        : 'Select a Host to display the settings supported for that Host type.';
                }
            }

            sourceTabs.forEach(tab => {
                if (tab.dataset.traditionalTaskEditorSourceTab === 'additional-commands') tab.hidden = !isFtp;
                if (tab.dataset.traditionalTaskEditorSourceTab === 'after-transfer') tab.hidden = isSyncTask();
            });
            if (sourceTabName === 'additional-commands' && !isFtp) setSourceTab('general');
            hostTabs.forEach(tab => {
                const name = tab.dataset.traditionalTaskEditorHostTab;
                if (name === 'authentication') tab.hidden = !(isMoveItTransfer || isFtp || isSftp || isS3 || isAzureBlob || isSharePoint || isGoogleStorage || isPop3);
                if (name === 'file-sorting') tab.hidden = !isMoveItTransfer;
                if (name === 'transfer') tab.hidden = !((isUnc && !isSyncTask()) || isFtp || isSftp);
                if (name === 'download-limits') tab.hidden = isSyncTask() || !(isLocal || isUnc || isMoveItTransfer || isFtp || isSftp || isS3 || isAzureBlob || isSharePoint || isGoogleStorage);
                if (name === 'security') tab.hidden = !(isFtp || isSftp);
                if (name === 'data-integrity') tab.hidden = !(isS3 || isAzureBlob);
                if (name === 'timeouts') tab.hidden = false;
            });
            dialog.querySelectorAll('[data-source-connect-timeout-only]').forEach(item => { item.hidden = !(isMoveItTransfer || isFtp || isSftp || isS3 || isAzureBlob || isSharePoint || isGoogleStorage || isPop3); });
            dialog.querySelectorAll('[data-source-data-timeout-only]').forEach(item => { item.hidden = !(isMoveItTransfer || isFtp || isS3 || isAzureBlob || isSharePoint || isGoogleStorage || isPop3); });
            dialog.querySelectorAll('[data-source-rescan-only]').forEach(item => { item.hidden = !(isLocal || isUnc || isFtp || isSftp || isS3 || isAzureBlob || isSharePoint || isGoogleStorage); });
            dialog.querySelectorAll('[data-source-unc-only]').forEach(item => { item.hidden = !isUnc; });
            dialog.querySelectorAll('[data-source-ftp-only]').forEach(item => { item.hidden = !isFtp; });
            dialog.querySelectorAll('[data-source-sftp-only]').forEach(item => { item.hidden = !isSftp; });
            dialog.querySelectorAll('[data-source-s3-general-only]').forEach(item => { item.hidden = !(isS3 || isGoogleStorage); });
            dialog.querySelectorAll('[data-source-azureblob-general-only]').forEach(item => { item.hidden = !isAzureBlob; });
            dialog.querySelectorAll('[data-source-sharepoint-general-only]').forEach(item => { item.hidden = !isSharePoint; });
            dialog.querySelectorAll('[data-source-sharepoint-auth-only]').forEach(item => { item.hidden = !isSharePoint; });
            dialog.querySelectorAll('[data-source-google-auth-only]').forEach(item => { item.hidden = !isGoogleStorage; });
            dialog.querySelectorAll('[data-source-email-auth-only]').forEach(item => { item.hidden = !isPop3; });
            dialog.querySelectorAll('[data-source-non-pop3]').forEach(item => { item.hidden = isPop3; });
            dialog.querySelectorAll('[data-source-credential-override-toggle]').forEach(item => { item.hidden = false; });
            dialog.querySelectorAll('[data-source-cloud-data-integrity-only]').forEach(item => { item.hidden = !(isS3 || isAzureBlob); });
            dialog.querySelectorAll('[data-source-s3-integrity-ifsure]').forEach(item => { item.hidden = !isS3; });
            if (sourceBucketBrowseButton) sourceBucketBrowseButton.disabled = !(isS3 || isGoogleStorage) || !source.hostId || !canBrowseHostStep('Source', source.hostTypeCode);
            if (sourceContainerBrowseButton) sourceContainerBrowseButton.disabled = !isAzureBlob || !source.hostId || !canBrowseHostStep('Source', source.hostTypeCode);
            if (sourceDocumentLibraryBrowseButton) sourceDocumentLibraryBrowseButton.disabled = !isSharePoint || !source.hostId || !canBrowseHostStep('Source', source.hostTypeCode);
            dialog.querySelectorAll('[data-source-cert-only]').forEach(item => { item.hidden = !(isMoveItTransfer || isFtp); });
            dialog.querySelectorAll('[data-source-md5-only]').forEach(item => { item.hidden = !(isFtp || isSftp); });
            const visibleNames = hostTabs.filter(tab => !tab.hidden).map(tab => tab.dataset.traditionalTaskEditorHostTab);
            if (!visibleNames.includes(hostTabName)) hostTabName = visibleNames[0] || 'timeouts';
            setHostTab(hostTabName);
        }

        function setSourceTab(name) {
            sourceTabName = name || 'general';
            sourceTabs.forEach(tab => {
                const selected = tab.dataset.traditionalTaskEditorSourceTab === sourceTabName;
                tab.classList.toggle('active', selected);
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            sourcePanels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorSourcePanel !== sourceTabName; });
        }

        function setHostTab(name) {
            hostTabName = name || 'timeouts';
            hostTabs.forEach(tab => {
                const selected = !tab.hidden && tab.dataset.traditionalTaskEditorHostTab === hostTabName;
                tab.classList.toggle('active', selected);
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            hostPanels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorHostPanel !== hostTabName; });
        }

        function activeStep() {
            return taskSteps.find(item => item.key === activeStepKey) || null;
        }

        function activeSource() {
            const step = activeStep();
            return step?.kind === 'Source' ? step.source : null;
        }

        function setSourceControlValue(field, value) {
            const controls = Array.from(dialog.querySelectorAll(`[data-source-field="${field}"]`));
            if (controls.length === 0) return;
            if (controls[0] instanceof HTMLInputElement && controls[0].type === 'radio') {
                controls.forEach(control => { control.checked = String(control.value) === String(value); });
                return;
            }
            const control = controls[0];
            if (control instanceof HTMLInputElement && control.type === 'checkbox') controls.forEach(item => { item.checked = Boolean(value); });
            else control.value = value == null ? '' : String(value);
        }

        function sourceControlValue(control) {
            if (control instanceof HTMLInputElement && control.type === 'checkbox' && control.dataset.sourceField === 'useWindowsCopyFileApi') return !control.checked;
            if (control instanceof HTMLInputElement && control.type === 'checkbox') return control.checked;
            if (control instanceof HTMLInputElement && control.type === 'radio') {
                if (control.dataset.sourceField === 'transferType') return String(control.value || '').toLowerCase();
                if (control.dataset.sourceField === 'passive') return String(control.value) === '1';
                if (control.dataset.sourceField === 'emailUseOAuth') return String(control.value) === '1';
                if (control.dataset.sourceField === 'dataIntegrityRule') return String(control.value || '').toLowerCase();
                return Number(control.value);
            }
            if (control instanceof HTMLInputElement && control.type === 'number') return control.value === '' ? null : Number(control.value);
            return String(control?.value ?? '');
        }

        function sourceValue(source, field) {
            return source?.[field];
        }

        function sourceStableKey(stepKey, field) {
            return `${stepKey}|${field}`;
        }

        function seedSourceStableValues(step) {
            if (!step?.source) return;
            Object.keys(step.source).forEach(field => {
                if (field === 'hostDefaults') return;
                sourceStableValues.set(sourceStableKey(step.key, field), cloneSimple(step.source[field]));
            });
        }

        async function confirmHostChange(control, kind) {
            if (!(control instanceof HTMLSelectElement)) return true;
            const field = kind === 'Source' ? control.dataset.sourceField : control.dataset.destinationField;
            if (field !== 'hostId') return true;
            const step = activeStep();
            const model = kind === 'Source' ? step?.source : step?.destination;
            if (!step || !model) return true;
            const stableMap = kind === 'Source' ? sourceStableValues : destinationStableValues;
            const stableKey = kind === 'Source' ? sourceStableKey(step.key, 'hostId') : destinationStableKey(step.key, 'hostId');
            const previous = String(stableMap.has(stableKey) ? stableMap.get(stableKey) : (model.hostId || ''));
            const next = String(control.value || '');
            if (previous === next || (!previous && next)) return true;
            const previousName = model.hostName || (previous ? `Host ID ${previous}` : 'no Host');
            const selected = control.options[control.selectedIndex];
            const nextName = selected?.textContent?.trim() || (next ? `Host ID ${next}` : 'no Host');
            const confirmed = await window.mauiConfirm?.({
                title: `Change ${kind} Host?`,
                message: `Change ${kind} Host from "${previousName}" to "${nextName}"? General${kind === 'Source' && !isSyncTask() ? ' and After Transfer' : ''} settings will be retained. Folder(s) and Host Override settings will be reset for the new Host.`,
                confirmText: 'Change Host'
            });
            if (!confirmed) {
                control.value = previous;
                return false;
            }
            return true;
        }

        async function changeHostTypeFilter(kind, select) {
            if (!(select instanceof HTMLSelectElement)) return;
            const step = activeStep();
            const model = kind === 'Source' ? step?.source : step?.destination;
            if (!step || !model) return;

            const filters = hostTypeFilterMap(kind);
            const requestedType = String(select.value || '').trim();
            const currentType = String(model.hostTypeCode || '').trim();

            if (!model.hostId) {
                filters.set(step.key, requestedType);
                if (kind === 'Source') populateSourceHostSelect(model);
                else populateDestinationHostSelect(model);
                return;
            }

            if (!requestedType || requestedType.toLowerCase() === currentType.toLowerCase()) {
                filters.set(step.key, currentType);
                select.value = currentType;
                if (kind === 'Source') populateSourceHostSelect(model);
                else populateDestinationHostSelect(model);
                return;
            }

            const confirmed = await window.mauiConfirm?.({
                title: `Change ${kind} Host Type?`,
                message: `Changing the ${kind} Host Type will clear the current Host "${model.hostName || `Host ID ${model.hostId}`}". General${kind === 'Source' && !isSyncTask() ? ' and After Transfer' : ''} settings will be retained. Folder(s) and Host Override settings will be reset.`,
                confirmText: 'Change Host Type'
            });
            if (!confirmed) {
                filters.set(step.key, currentType);
                select.value = currentType;
                if (kind === 'Source') populateSourceHostSelect(model);
                else populateDestinationHostSelect(model);
                return;
            }

            const before = deepClone(model);
            model.hostId = '';
            model.hostName = '';
            model.hostTypeCode = '';
            model.hostType = '';
            model.hostDefaults = normalizeHostDefaults({});
            filters.set(step.key, requestedType);

            if (kind === 'Source') {
                resetSourceHostSpecificState(model);
                lastUndo = { kind: 'source-snapshot', stepKey: step.key, before, label: 'Source Host Type' };
                seedSourceStableValues(step);
                renderSource(step);
            } else {
                resetDestinationHostSpecificState(model);
                lastUndo = { kind: 'destination-snapshot', stepKey: step.key, before, label: 'Destination Host Type' };
                seedDestinationStableValues(step);
                renderDestination(step);
            }

            updateStepSummary(step);
            updateUndoState();
            updateDirtyState();
        }

        function applySourceControl(control, commit) {
            const step = activeStep();
            const source = step?.source;
            const field = control?.dataset?.sourceField;
            if (!step || !source || !field) return;

            const after = sourceControlValue(control);
            const stableKey = sourceStableKey(step.key, field);
            const before = sourceStableValues.has(stableKey) ? sourceStableValues.get(stableKey) : cloneSimple(sourceValue(source, field));

            const snapshotFields = new Set([
                'hostId', 'overrideRetryCount', 'overrideRetryTimeout', 'overrideRescanTime',
                'overrideMaxFiles', 'overrideMaxBytes', 'overrideWindowsCopyFileApi',
                'overrideCredentials', 'overrideClientCertificate', 'overrideClientKey', 'overrideFileSorting',
                'overrideConnectTimeout', 'overrideDataTimeout', 'sortField', 'sortDirection',
                'overrideTransferType', 'overridePassive', 'overrideXSha1', 'overrideBlindDownload',
                'overrideResume', 'overrideReuseTlsSession', 'overrideMd5Mode', 'overrideMd5FileName'
            ]);
            const beforeSource = snapshotFields.has(field) ? deepClone(source) : null;
            applySourceValue(source, field, after);

            if (commit && !sameValue(before, sourceValue(source, field))) {
                lastUndo = beforeSource
                    ? { kind: 'source-snapshot', stepKey: step.key, before: beforeSource, label: sourceFieldLabel(field) }
                    : { kind: 'source-field', stepKey: step.key, field, before, label: sourceFieldLabel(field) };
                seedSourceStableValues(step);
                updateUndoState();
            }

            updateStepSummary(step);
            refreshSourceDependencies(source);
            updateDirtyState();
        }

        function resetSourceHostSpecificState(source) {
            source.path = '';
            source.useDefaultBucket = true;
            source.bucket = source.hostDefaults.bucket || '';
            source.useDefaultContainer = true;
            source.container = source.hostDefaults.container || '';
            source.useDefaultDocumentLibrary = true;
            source.documentLibrary = source.hostDefaults.documentLibrary || '';
            source.documentLibraryGuid = source.hostDefaults.documentLibraryGuid || '';
            source.requiresFolderSelection = Boolean(source.hostId) && hostWriteProfile('Source', source.hostTypeCode) !== HOST_WRITE_PROFILE_POP3;
            source.overrideRetryCount = false;
            source.retryCount = source.hostDefaults.retryCount;
            source.overrideRetryTimeout = false;
            source.retryTimeoutSeconds = source.hostDefaults.retryTimeoutSeconds;
            source.overrideRescanTime = false;
            source.rescanSeconds = source.hostDefaults.rescanSeconds;
            source.overrideMaxFiles = false;
            source.maxFiles = source.hostDefaults.maxFiles;
            source.overrideMaxBytes = false;
            source.maxBytes = source.hostDefaults.maxBytes;
            source.overridePreserveFileTimestamp = isSyncTask() && activeStep()?.index === 1;
            source.preserveFileTimestamp = isSyncTask() && activeStep()?.index === 1;
            source.overrideWindowsCopyFileApi = false;
            source.useWindowsCopyFileApi = source.hostDefaults.useWindowsCopyFileApi;
            source.overrideCredentials = false;
            source.username = source.hostDefaults.username;
            source.passwordConfigured = false;
            source.password = '';
            source.overrideClientCertificate = false;
            source.clientCertificateSha1 = source.hostDefaults.clientCertificateSha1;
            source.clientCertificateStore = source.hostDefaults.clientCertificateStore;
            source.clientCertificateSubject = source.hostDefaults.clientCertificateSubject;
            source.sharePointCertificateSha1 = source.hostDefaults.clientCertificateSha1 || '';
            source.sharePointAuthenticationMode = source.hostDefaults.sharePointAuthenticationMode ?? 2;
            source.sharePointClientId = source.hostDefaults.sharePointClientId || '';
            source.sharePointTenantId = source.hostDefaults.sharePointTenantId || '';
            source.sharePointRealmId = source.hostDefaults.sharePointRealmId || '';
            source.sharePointClientSecretConfigured = false;
            source.sharePointClientSecret = '';
            source.googleUseOAuth = source.hostDefaults.googleUseOAuth ?? false;
            source.googleProjectId = source.hostDefaults.googleProjectId || '';
            source.googleOAuthClientId = source.hostDefaults.googleOAuthClientId || '';
            source.googleOAuthClientSecretConfigured = false;
            source.googleOAuthClientSecret = '';
            source.emailUseOAuth = source.hostDefaults.emailUseOAuth ?? false;
            source.emailOAuthType = source.hostDefaults.emailOAuthTenantId ? 'microsoft365' : 'custom';
            source.emailOAuthUsername = source.hostDefaults.username || '';
            source.emailOAuthClientId = source.hostDefaults.emailOAuthClientId || '';
            source.emailOAuthScope = source.hostDefaults.emailOAuthScope || '';
            source.emailOAuthTokenUrl = source.hostDefaults.emailOAuthTokenUrl || '';
            source.emailOAuthTenantId = source.hostDefaults.emailOAuthTenantId || '';
            source.emailOAuthClientSecretConfigured = false;
            source.emailOAuthClientSecret = '';
            source.overrideClientKey = false;
            source.clientKeyId = source.hostDefaults.clientKeyId || '0';
            source.overrideFileSorting = false;
            source.sort = source.hostDefaults.sort;
            source.overrideConnectTimeout = false;
            source.connectTimeoutSeconds = source.hostDefaults.connectTimeoutSeconds;
            source.overrideDataTimeout = false;
            source.dataTimeoutSeconds = source.hostDefaults.dataTimeoutSeconds;
            source.additionalCommandsBefore = '';
            source.additionalCommandsAfter = '';
            source.overrideTransferType = false;
            source.transferType = source.hostDefaults.transferType || 'binary';
            source.overridePassive = false;
            source.passive = source.hostDefaults.passive;
            source.overrideXSha1 = false;
            source.useXSha1 = source.hostDefaults.useXSha1;
            source.overrideBlindDownload = false;
            source.blindDownload = source.hostDefaults.blindDownload;
            source.overrideResume = false;
            source.resume = source.hostDefaults.resume;
            source.overrideReuseTlsSession = false;
            source.reuseTlsSession = source.hostDefaults.reuseTlsSession;
            source.overrideMd5Mode = false;
            source.md5Mode = source.hostDefaults.md5Mode || 'Never';
            source.overrideMd5FileName = false;
            source.md5FileName = source.hostDefaults.md5FileName || 'MD5SUM';
            source.overrideDataIntegrity = false;
            source.dataIntegrityRule = source.hostDefaults.dataIntegrityRule || 'never';
            source.account = '';
            source.accountConfigured = false;
        }

        function applySourceValue(source, field, value) {
            const previousHostId = String(source.hostId || '');
            source[field] = value;
            if (field === 'hostId') {
                const host = sourceHosts.find(item => item.id === String(value));
                source.hostId = String(value || '');
                source.hostName = host?.name || '';
                source.hostTypeCode = host?.typeCode || '';
                source.hostType = host?.type || '';
                source.hostDefaults = normalizeHostDefaults(host?.defaults || {});
                if (host?.typeCode) sourceHostTypeFilters.set(activeStep()?.key || '', host.typeCode);
                if (previousHostId !== source.hostId) resetSourceHostSpecificState(source);
                populateSourceHostSelect(source);
            }
            if (field === 'path' && String(source.path || '').trim()) source.requiresFolderSelection = false;
            if (field === 'bucket') {
                const nextBucket = String(value || '').trim();
                source.useDefaultBucket = nextBucket === String(source.hostDefaults.bucket || '').trim();
                source.bucket = nextBucket;
            }
            if (field === 'container') {
                const nextContainer = String(value || '').trim();
                source.useDefaultContainer = nextContainer === String(source.hostDefaults.container || '').trim();
                source.container = nextContainer;
            }
            if (field === 'documentLibrary') {
                const nextLibrary = String(value || '').trim();
                source.useDefaultDocumentLibrary = nextLibrary === String(source.hostDefaults.documentLibrary || '').trim()
                    && String(source.documentLibraryGuid || '').trim() === String(source.hostDefaults.documentLibraryGuid || '').trim();
                source.documentLibrary = nextLibrary;
            }
            if (field === 'ignoreFolders' && !value) {
                const fieldControl = dialog.querySelector('[data-source-field="excludeFolders"]');
                if (fieldControl) fieldControl.disabled = true;
            }
            if (field === 'ignoreFiles' && !value) {
                const fieldControl = dialog.querySelector('[data-source-field="excludeFiles"]');
                if (fieldControl) fieldControl.disabled = true;
            }
            if (field === 'overrideRetryCount' && value && source.retryCount == null) source.retryCount = source.hostDefaults.retryCount;
            if (field === 'overrideRetryTimeout' && value && source.retryTimeoutSeconds == null) source.retryTimeoutSeconds = source.hostDefaults.retryTimeoutSeconds;
            if (field === 'overrideRescanTime' && value && source.rescanSeconds == null) source.rescanSeconds = source.hostDefaults.rescanSeconds;
            if (field === 'overrideMaxFiles' && value && source.maxFiles == null) source.maxFiles = source.hostDefaults.maxFiles;
            if (field === 'overrideMaxBytes' && value && source.maxBytes == null) source.maxBytes = source.hostDefaults.maxBytes;
            if (field === 'overrideWindowsCopyFileApi' && value && source.useWindowsCopyFileApi == null) source.useWindowsCopyFileApi = source.hostDefaults.useWindowsCopyFileApi;
            if (field === 'overrideCredentials' && value && !String(source.username || '').trim()) source.username = source.hostDefaults.username;
            if (field === 'overrideCredentials' && value && hostWriteProfile('Source', source.hostTypeCode) === HOST_WRITE_PROFILE_POP3) {
                if (source.emailUseOAuth == null) source.emailUseOAuth = source.hostDefaults.emailUseOAuth ?? false;
                if (!String(source.emailOAuthUsername || '').trim()) source.emailOAuthUsername = source.hostDefaults.username || '';
                if (!String(source.emailOAuthClientId || '').trim()) source.emailOAuthClientId = source.hostDefaults.emailOAuthClientId || '';
                if (!String(source.emailOAuthScope || '').trim()) source.emailOAuthScope = source.hostDefaults.emailOAuthScope || '';
                if (!String(source.emailOAuthTokenUrl || '').trim()) source.emailOAuthTokenUrl = source.hostDefaults.emailOAuthTokenUrl || '';
                if (!String(source.emailOAuthTenantId || '').trim()) source.emailOAuthTenantId = source.hostDefaults.emailOAuthTenantId || '';
                if (!String(source.emailOAuthType || '').trim()) source.emailOAuthType = source.emailOAuthTenantId ? 'microsoft365' : 'custom';
            }
            if (field === 'emailUseOAuth' && value) {
                if (!String(source.emailOAuthUsername || '').trim()) source.emailOAuthUsername = source.hostDefaults.username || source.username || '';
                if (!String(source.emailOAuthClientId || '').trim()) source.emailOAuthClientId = source.hostDefaults.emailOAuthClientId || '';
                if (!String(source.emailOAuthTenantId || '').trim()) source.emailOAuthTenantId = source.hostDefaults.emailOAuthTenantId || '';
                if (!String(source.emailOAuthScope || '').trim()) source.emailOAuthScope = source.hostDefaults.emailOAuthScope || 'https://outlook.office365.com/.default';
                if (!String(source.emailOAuthTokenUrl || '').trim()) source.emailOAuthTokenUrl = source.hostDefaults.emailOAuthTokenUrl || '';
                if (!String(source.emailOAuthType || '').trim()) source.emailOAuthType = source.emailOAuthTenantId ? 'microsoft365' : 'custom';
            }
            if (field === 'emailOAuthType' && value === 'microsoft365' && !String(source.emailOAuthScope || '').trim()) source.emailOAuthScope = 'https://outlook.office365.com/.default';
            if (field === 'overrideTransferType' && value && !String(source.transferType || '').trim()) source.transferType = source.hostDefaults.transferType || 'binary';
            if (field === 'overridePassive' && value && source.passive == null) source.passive = source.hostDefaults.passive ?? true;
            if (field === 'overrideXSha1' && value && source.useXSha1 == null) source.useXSha1 = source.hostDefaults.useXSha1 ?? false;
            if (field === 'overrideBlindDownload' && value && source.blindDownload == null) source.blindDownload = source.hostDefaults.blindDownload ?? false;
            if (field === 'overrideResume' && value && source.resume == null) source.resume = source.hostDefaults.resume ?? false;
            if (field === 'overrideReuseTlsSession' && value && source.reuseTlsSession == null) source.reuseTlsSession = source.hostDefaults.reuseTlsSession ?? false;
            if (field === 'overrideMd5Mode' && value && !String(source.md5Mode || '').trim()) source.md5Mode = source.hostDefaults.md5Mode || 'Never';
            if (field === 'overrideMd5FileName' && value && !String(source.md5FileName || '').trim()) source.md5FileName = source.hostDefaults.md5FileName || 'MD5SUM';
            if (field === 'overrideDataIntegrity' && value && !String(source.dataIntegrityRule || '').trim()) source.dataIntegrityRule = source.hostDefaults.dataIntegrityRule || 'never';
            if (field === 'overrideClientKey' && value && !String(source.clientKeyId || '').trim()) source.clientKeyId = source.hostDefaults.clientKeyId || '0';
            if (field === 'overrideClientCertificate' && value && !String(source.clientCertificateSha1 || '').trim()) {
                source.clientCertificateSha1 = source.hostDefaults.clientCertificateSha1;
                source.clientCertificateStore = source.hostDefaults.clientCertificateStore;
                source.clientCertificateSubject = source.hostDefaults.clientCertificateSubject;
            }
            if (field === 'clientCertificateSha1') {
                const certificate = tlsCertificates.find(item => item.sha1Thumbprint.replaceAll(' ', '').toLowerCase() === String(value || '').replaceAll(' ', '').toLowerCase());
                source.clientCertificateStore = certificate?.store || (value ? source.clientCertificateStore || 'My' : '');
                source.clientCertificateSubject = certificate?.name || (value ? source.clientCertificateSubject : '');
            }
            if (field === 'sharePointCertificateSha1') {
                const certificate = tlsCertificates.find(item => item.sha1Thumbprint.replaceAll(' ', '').toLowerCase() === String(value || '').replaceAll(' ', '').toLowerCase());
                source.sharePointCertificateSha1 = String(value || '');
                source.clientCertificateSha1 = String(value || '');
                source.clientCertificateStore = certificate?.store || (value ? source.clientCertificateStore || 'My' : '');
                source.clientCertificateSubject = certificate?.name || (value ? source.clientCertificateSubject : '');
            }
            if (field === 'overrideFileSorting' && value && !String(source.sort || '').trim()) source.sort = source.hostDefaults.sort || 'Filename,A';
            if (field === 'sortField' || field === 'sortDirection') {
                const current = splitSort(source.sort || source.hostDefaults.sort);
                const nextField = field === 'sortField' ? String(value || 'Filename') : current.field;
                const nextDirection = field === 'sortDirection' ? String(value || 'A') : current.direction;
                source.sort = `${nextField},${nextDirection}`;
            }
            if (field === 'overrideConnectTimeout' && value && source.connectTimeoutSeconds == null) source.connectTimeoutSeconds = source.hostDefaults.connectTimeoutSeconds;
            if (field === 'overrideDataTimeout' && value && source.dataTimeoutSeconds == null) source.dataTimeoutSeconds = source.hostDefaults.dataTimeoutSeconds;

            if (field.startsWith('override') || field === 'hostId' || field === 'clientCertificateSha1' || field === 'sharePointCertificateSha1' || field === 'clientKeyId' || field === 'bucket' || field === 'container' || field === 'documentLibrary' || field.startsWith('sharePoint') || field === 'emailUseOAuth' || field.startsWith('emailOAuth')) {
                renderSource(activeStep());
            }
        }

        function sourceFieldLabel(field) {
            const labels = {
                hostId: 'Source Host', bucket: 'Bucket Name', container: 'Container', documentLibrary: 'Document Library', path: 'Source Folder', fileMask: 'Source File Mask', ignoreFolders: 'Ignore Folders', excludeFolders: 'Ignored Folders',
                searchSubdirectories: 'Search Subdirectories', ignoreFiles: 'Ignore Files', excludeFiles: 'Ignored Files', hasSpecialFilters: 'Special Filters',
                collectOnlyNewFiles: 'Collect Only New Files', expandZipFiles: 'Expand Zip Files', retryIfNoFilesFound: 'Retry If No Files Found',
                deleteOriginalAction: 'After Transfer Action', renameTo: 'Rename Original File', renamedFilesMayOverwrite: 'Rename Overwrite',
                overrideRetryCount: 'Retry Count Override', retryCount: 'Retry Count', overrideRetryTimeout: 'Retry Timeout Override', retryTimeoutSeconds: 'Retry Timeout',
                overrideRescanTime: 'Transfer Rescan Override', rescanSeconds: 'Transfer Rescan Time', overrideMaxFiles: 'File Count Limit Override', maxFiles: 'File Count Limit',
                overrideMaxBytes: 'File Size Limit Override', maxBytes: 'File Size Limit', overrideWindowsCopyFileApi: 'Windows CopyFile API Override', useWindowsCopyFileApi: 'Windows CopyFile API',
                overrideCredentials: 'Credentials Override', username: 'Username', password: 'Password', account: 'FTP Account', emailUseOAuth: 'POP3 Authentication', emailOAuthType: 'POP3 OAuth Type', emailOAuthUsername: 'POP3 OAuth Username', emailOAuthClientId: 'POP3 OAuth Client ID', emailOAuthClientSecret: 'POP3 OAuth Client Secret', emailOAuthTenantId: 'POP3 OAuth Tenant ID', emailOAuthScope: 'POP3 OAuth Authorization Scope', emailOAuthTokenUrl: 'POP3 OAuth Token URL', sharePointAuthenticationMode: 'SharePoint Authentication', sharePointClientId: 'SharePoint Client ID', sharePointTenantId: 'SharePoint Tenant ID', sharePointCertificateSha1: 'SharePoint Certificate',
                overrideClientCertificate: 'Client Certificate Override', clientCertificateSha1: 'Client Certificate',
                overrideClientKey: 'Client Key Override', clientKeyId: 'Client Key',
                overrideClientKey: 'Client Key Override', clientKeyId: 'Client Key',
                overrideFileSorting: 'File Sorting Override', sortField: 'Sort Field', sortDirection: 'Sort Direction',
                overrideConnectTimeout: 'Connect Timeout Override', connectTimeoutSeconds: 'Connect Timeout',
                overrideDataTimeout: 'Data Timeout Override', dataTimeoutSeconds: 'Data Timeout',
                additionalCommandsBefore: 'FTP Commands Before Transfer', additionalCommandsAfter: 'FTP Commands After Transfer',
                overrideTransferType: 'FTP Transfer Type Override', transferType: 'FTP Transfer Type',
                overridePassive: 'FTP Transfer Mode Override', passive: 'FTP Transfer Mode',
                overrideXSha1: 'FTP XSHA1 Override', useXSha1: 'FTP XSHA1',
                overrideBlindDownload: 'Blind Download Override', blindDownload: 'Blind Download',
                overrideResume: 'Resume Override', resume: 'Resume',
                overrideReuseTlsSession: 'FTP Reuse TLS Session Override', reuseTlsSession: 'FTP Reuse TLS Session',
                overrideMd5Mode: 'Use MD5 Override', md5Mode: 'Use MD5',
                overrideMd5FileName: 'MD5 File Name Override', md5FileName: 'MD5 File Name',
                overrideDataIntegrity: 'Data Integrity Override', dataIntegrityRule: 'Data Integrity Rule'
            };
            return labels[field] || field;
        }

        function setProcessTab(name) {
            processTabName = name || 'general';
            processTabs.forEach(tab => {
                const selected = tab.dataset.traditionalTaskEditorProcessTab === processTabName;
                tab.classList.toggle('active', selected);
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            processPanels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorProcessPanel !== processTabName; });
        }

        function populateProcessScriptSelect(process) {
            if (!processScriptSelect) return;
            processScriptSelect.replaceChildren(new Option('Select a Script', ''));
            scriptOptions.forEach(script => processScriptSelect.appendChild(new Option(`${script.name} — ${script.type}`, script.id)));
            if (process.scriptId && !scriptOptions.some(script => script.id === process.scriptId)) {
                processScriptSelect.appendChild(new Option(process.scriptName || `Script ID ${process.scriptId}`, process.scriptId));
            }
            processScriptSelect.value = process.scriptId || '';
            if (processScriptHelp) {
                const script = scriptOptions.find(item => item.id === process.scriptId);
                processScriptHelp.textContent = script?.type || process.scriptType || 'Select the MOVEit Script executed by this Process.';
            }
        }

        function renderProcess(step) {
            const process = step.process;
            if (!process) return;
            populateProcessScriptSelect(process);
            setProcessTab(processTabName);
            setProcessControlValue('run', process.run || 'PerFile');
            setProcessControlValue('isDestination', process.isDestination);
            renderProcessParameters(process);
            scriptGuidance?.show(process);
        }

        function renderProcessParameters(process) {
            renderParameterRows(process?.parameters, processParameterRows, processParameterTable, processParametersEmpty, {
                editDatasetKey: 'traditionalTaskEditorProcessParameterEdit',
                secureDatasetKey: 'traditionalTaskEditorProcessParameterSecure',
                removeDatasetKey: 'traditionalTaskEditorProcessParameterRemove',
                secureEditLabel: 'Edit',
                hideTableWhenEmpty: true
            });
            if (isAdvancedTask()) scriptGuidance?.appendRequiredRows('process', process, processParameterRows, processParameterTable, processParametersEmpty);
        }

        function clearProcessParameterError() {
            if (!processParameterError) return;
            processParameterError.hidden = true;
            processParameterError.textContent = '';
        }

        function showProcessParameterError(message) {
            if (!processParameterError) return;
            processParameterError.textContent = message || 'Unable to update the Process parameter.';
            processParameterError.hidden = false;
        }

        function openProcessParameterEditor(parameterName = '') {
            const step = activeStep();
            const process = step?.kind === 'Process' ? step.process : null;
            if (!process || !processParameterDialog) return;
            const normalizedName = String(parameterName || '').trim();
            const existing = process.parameters.find(item => item.name.toLowerCase() === normalizedName.toLowerCase()) || null;
            processParameterEditName = existing?.name || '';
            const definition = scriptGuidance?.definition(normalizedName, 'process');
            scriptGuidance?.parameterHelp(processParameterDialog.querySelector('[data-process-script-parameter-help]'), definition);
            clearProcessParameterError();
            const preservedSecure = Boolean(existing?.secure && !existing.valueChanged);
            if (processParameterTitle) processParameterTitle.textContent = existing ? 'Edit Process Parameter' : 'Add Process Parameter';
            if (processParameterSubtitle) processParameterSubtitle.textContent = preservedSecure
                ? 'Secure parameter values cannot be viewed or edited. You can rename the parameter or reset its value.'
                : 'Define a parameter passed to this Process step.';
            if (processParameterName) {
                processParameterName.value = existing?.name || normalizedName;
                processParameterName.readOnly = !existing && Boolean(definition);
            }
            if (processParameterValue) {
                processParameterValue.value = preservedSecure ? '' : (existing?.value ?? definition?.defaultValue ?? '');
                processParameterValue.disabled = preservedSecure;
                processParameterValue.placeholder = preservedSecure ? '(suppressed)' : 'Value';
            }
            if (processParameterSecure) {
                processParameterSecure.checked = Boolean(existing?.secure);
                processParameterSecure.disabled = preservedSecure;
            }
            if (processParameterReset) processParameterReset.hidden = !preservedSecure;
            processParameterDialog.showModal();
            processParameterName?.focus();
        }

        function saveProcessParameter() {
            const step = activeStep();
            const process = step?.kind === 'Process' ? step.process : null;
            if (!step || !process) return;
            const name = String(processParameterName?.value || '').trim();
            const value = String(processParameterValue?.value || '');
            const secure = Boolean(processParameterSecure?.checked);
            const nameError = validateParameterName(name, 'Parameter');
            if (nameError) {
                showProcessParameterError(nameError);
                processParameterName?.focus();
                return;
            }
            const duplicate = process.parameters.find(item => item.name.toLowerCase() === name.toLowerCase() && item.name.toLowerCase() !== processParameterEditName.toLowerCase());
            if (duplicate) {
                showProcessParameterError(`A Process parameter named "${name}" already exists.`);
                processParameterName?.focus();
                return;
            }
            const existing = process.parameters.find(item => item.name.toLowerCase() === processParameterEditName.toLowerCase()) || null;
            const preserveSecureValue = Boolean(existing?.secure && !existing.valueChanged);
            const definition = scriptGuidance?.definition(name, 'process');
            if (definition?.required && !preserveSecureValue && !value.length && !definition.defaultValue) {
                showProcessParameterError(`Enter a value for required parameter ${name}.`); processParameterValue?.focus(); return;
            }
            const before = deepClone(process);
            const replacement = {
                name,
                originalName: existing?.originalName || existing?.name || name,
                value: preserveSecureValue ? '' : value,
                secure: preserveSecureValue ? true : secure,
                valueChanged: preserveSecureValue ? false : true
            };
            if (existing) {
                const index = process.parameters.indexOf(existing);
                process.parameters.splice(index, 1, replacement);
            } else {
                process.parameters.push(replacement);
            }
            lastUndo = { kind: 'process-snapshot', stepKey: step.key, before, label: existing ? 'Edit Process Parameter' : 'Add Process Parameter' };
            processParameterEditName = '';
            processParameterDialog?.close();
            seedProcessStableValues(step);
            renderProcessParameters(process);
            scriptGuidance?.show(process);
            updateUndoState();
            updateDirtyState();
        }

        function resetProcessParameterValue() {
            const step = activeStep();
            const process = step?.kind === 'Process' ? step.process : null;
            if (!step || !process) return;
            const existing = process.parameters.find(item => item.name.toLowerCase() === processParameterEditName.toLowerCase()) || null;
            if (!existing?.secure) return;
            const name = String(processParameterName?.value || existing.name).trim();
            const nameError = validateParameterName(name, 'Parameter');
            if (nameError) { showProcessParameterError(nameError); processParameterName?.focus(); return; }
            const duplicate = process.parameters.find(item => item !== existing && item.name.toLowerCase() === name.toLowerCase());
            if (duplicate) { showProcessParameterError(`A Process parameter named "${name}" already exists.`); processParameterName?.focus(); return; }
            const before = deepClone(process);
            const replacement = { name, originalName: existing.originalName || existing.name, value: '', secure: true, valueChanged: true };
            process.parameters.splice(process.parameters.indexOf(existing), 1, replacement);
            lastUndo = { kind: 'process-snapshot', stepKey: step.key, before, label: `Reset Process Parameter ${name}` };
            processParameterEditName = '';
            processParameterDialog?.close();
            seedProcessStableValues(step);
            renderProcessParameters(process);
            scriptGuidance?.show(process);
            updateUndoState();
            updateDirtyState();
        }

        function removeProcessParameter(parameterName) {
            const step = activeStep();
            const process = step?.kind === 'Process' ? step.process : null;
            if (!step || !process) return;
            const index = process.parameters.findIndex(item => item.name.toLowerCase() === String(parameterName || '').trim().toLowerCase());
            if (index < 0) return;
            const before = deepClone(process);
            process.parameters.splice(index, 1);
            lastUndo = { kind: 'process-snapshot', stepKey: step.key, before, label: 'Remove Process Parameter' };
            seedProcessStableValues(step);
            renderProcessParameters(process);
            scriptGuidance?.show(process);
            updateUndoState();
            updateDirtyState();
        }

        function setProcessControlValue(field, value) {
            const controls = Array.from(dialog.querySelectorAll(`[data-process-field="${field}"]`));
            if (controls.length === 0) return;
            if (controls[0] instanceof HTMLInputElement && controls[0].type === 'radio') {
                controls.forEach(control => { control.checked = String(control.value).toLowerCase() === String(value).toLowerCase(); });
                return;
            }
            const control = controls[0];
            if (control instanceof HTMLInputElement && control.type === 'checkbox') control.checked = Boolean(value);
            else control.value = value == null ? '' : String(value);
        }

        function processControlValue(control) {
            if (control instanceof HTMLInputElement && control.type === 'checkbox') return control.checked;
            return String(control?.value ?? '');
        }

        function processStableKey(stepKey, field) { return `${stepKey}|${field}`; }

        function seedProcessStableValues(step) {
            if (!step?.process) return;
            Object.keys(step.process).forEach(field => {
                if (field === 'parameters') return;
                processStableValues.set(processStableKey(step.key, field), cloneSimple(step.process[field]));
            });
        }

        function applyProcessControl(control, commit) {
            const step = activeStep();
            const process = step?.process;
            const field = control?.dataset?.processField;
            if (!step || !process || !field) return;
            const after = processControlValue(control);
            const stableKey = processStableKey(step.key, field);
            const before = processStableValues.has(stableKey) ? processStableValues.get(stableKey) : cloneSimple(process[field]);
            const beforeProcess = field === 'scriptId' ? deepClone(process) : null;
            process[field] = after;
            if (field === 'scriptId') {
                const script = scriptOptions.find(item => item.id === String(after));
                process.scriptId = String(after || '');
                process.scriptName = script?.name || '';
                process.scriptType = script?.type || '';
                populateProcessScriptSelect(process);
                scriptGuidance?.show(process);
                renderTaskParameters();
                renderProcessParameters(process);
            }
            if (commit && !sameValue(before, process[field])) {
                lastUndo = beforeProcess
                    ? { kind: 'process-snapshot', stepKey: step.key, before: beforeProcess, label: processFieldLabel(field) }
                    : { kind: 'process-field', stepKey: step.key, field, before, label: processFieldLabel(field) };
                seedProcessStableValues(step);
                updateUndoState();
            }
            updateStepSummary(step);
            updateDirtyState();
        }

        function processFieldLabel(field) {
            return ({ scriptId: 'Process Script', run: 'Process Run Mode', isDestination: 'Use Process as Destination' })[field] || field;
        }

        function setDestinationTab(name) {
            destinationTabName = name || 'general';
            destinationTabs.forEach(tab => {
                const selected = tab.dataset.traditionalTaskEditorDestinationTab === destinationTabName;
                tab.classList.toggle('active', selected);
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            destinationPanels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorDestinationPanel !== destinationTabName; });
        }

        function setDestinationHostTab(name) {
            destinationHostTabName = name || 'timeouts';
            destinationHostTabs.forEach(tab => {
                const selected = !tab.hidden && tab.dataset.traditionalTaskEditorDestinationHostTab === destinationHostTabName;
                tab.classList.toggle('active', selected);
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            destinationHostPanels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorDestinationHostPanel !== destinationHostTabName; });
        }

        function renderDestination(step) {
            const destination = step.destination;
            clearHostStepWriteAvailability('Destination');
            if (!destination) return;
            populateDestinationHostSelect(destination);
            setDestinationTab(destinationTabName);
            setDestinationControlValue('path', destination.path);
            setDestinationControlValue('bucket', destination.useDefaultBucket ? destination.hostDefaults.bucket : destination.bucket);
            setDestinationControlValue('container', destination.useDefaultContainer ? destination.hostDefaults.container : destination.container);
            setDestinationControlValue('documentLibrary', destination.useDefaultDocumentLibrary ? destination.hostDefaults.documentLibrary : destination.documentLibrary);
            setDestinationControlValue('sharePointAuthenticationMode', effectiveOverrideValue(destination.overrideCredentials, destination.sharePointAuthenticationMode, destination.hostDefaults.sharePointAuthenticationMode) ?? 2);
            setDestinationControlValue('sharePointClientId', effectiveOverrideValue(destination.overrideCredentials, destination.sharePointClientId, destination.hostDefaults.sharePointClientId));
            setDestinationControlValue('sharePointTenantId', effectiveOverrideValue(destination.overrideCredentials, destination.sharePointTenantId, destination.hostDefaults.sharePointTenantId));
            setDestinationControlValue('addressTo', destination.addressTo);
            setDestinationControlValue('subject', destination.subject);
            setDestinationControlValue('isHtml', destination.isHtml ? 1 : 0);
            if (hostWriteProfile('Destination', destination.hostTypeCode) === HOST_WRITE_PROFILE_SMTP)
                setDestinationSmtpMessageContent(destination);
            setDestinationControlValue('useRelativeSubdirectories', destination.useRelativeSubdirectories);
            setDestinationControlValue('forceDirectory', destination.forceDirectory);
            setDestinationControlValue('zip', destination.zip);
            setDestinationControlValue('useOriginalName', destination.useOriginalName);
            setDestinationControlValue('fileName', destination.fileName);
            setDestinationControlValue('overwriteAction', destination.overwriteAction);
            setDestinationControlValue('overrideRetryCount', destination.overrideRetryCount);
            setDestinationControlValue('retryCount', effectiveOverrideValue(destination.overrideRetryCount, destination.retryCount, destination.hostDefaults.retryCount));
            setDestinationControlValue('overrideRetryTimeout', destination.overrideRetryTimeout);
            setDestinationControlValue('retryTimeoutSeconds', effectiveOverrideValue(destination.overrideRetryTimeout, destination.retryTimeoutSeconds, destination.hostDefaults.retryTimeoutSeconds));
            setDestinationControlValue('overrideWindowsCopyFileApi', destination.overrideWindowsCopyFileApi);
            setDestinationControlValue('useWindowsCopyFileApi', windowsCopyFileEnabled(effectiveOverrideValue(destination.overrideWindowsCopyFileApi, destination.useWindowsCopyFileApi, destination.hostDefaults.useWindowsCopyFileApi)));
            setDestinationControlValue('overridePreserveFileTimestamp', destination.overridePreserveFileTimestamp);
            setDestinationControlValue('preserveFileTimestamp', effectiveOverrideValue(destination.overridePreserveFileTimestamp, destination.preserveFileTimestamp, destination.hostDefaults.preserveFileTimestamp));
            setDestinationControlValue('overrideCredentials', destination.overrideCredentials);
            setDestinationControlValue('username', effectiveOverrideValue(destination.overrideCredentials, destination.username, destination.hostDefaults.username));
            setDestinationControlValue('password', destination.password);
            setDestinationControlValue('overrideClientCertificate', destination.overrideClientCertificate);
            populateClientCertificateSelect('Destination', destination);
            populateSharePointCertificateSelect('Destination', destination);
            setDestinationControlValue('sharePointCertificateSha1', destination.sharePointCertificateSha1 || destination.clientCertificateSha1);
            setDestinationControlValue('sharePointRealmId', destination.sharePointRealmId);
            setDestinationControlValue('sharePointClientSecret', destination.sharePointClientSecret);
            setDestinationControlValue('googleProjectId', destination.googleProjectId);
            setDestinationControlValue('googleOAuthClientId', destination.googleOAuthClientId);
            setDestinationControlValue('googleOAuthClientSecret', destination.googleOAuthClientSecret);
            setDestinationControlValue('emailUseOAuth', destination.overrideCredentials ? destination.emailUseOAuth : destination.hostDefaults.emailUseOAuth);
            setDestinationControlValue('emailOAuthType', destination.overrideCredentials ? destination.emailOAuthType : (destination.hostDefaults.emailOAuthTenantId ? 'microsoft365' : 'custom'));
            setDestinationControlValue('emailOAuthUsername', destination.overrideCredentials ? destination.emailOAuthUsername : destination.hostDefaults.username);
            setDestinationControlValue('emailOAuthClientId', destination.overrideCredentials ? destination.emailOAuthClientId : destination.hostDefaults.emailOAuthClientId);
            setDestinationControlValue('emailOAuthScope', destination.overrideCredentials ? destination.emailOAuthScope : destination.hostDefaults.emailOAuthScope);
            setDestinationControlValue('emailOAuthTokenUrl', destination.overrideCredentials ? destination.emailOAuthTokenUrl : destination.hostDefaults.emailOAuthTokenUrl);
            setDestinationControlValue('emailOAuthTenantId', destination.overrideCredentials ? destination.emailOAuthTenantId : destination.hostDefaults.emailOAuthTenantId);
            setDestinationControlValue('emailOAuthClientSecret', destination.emailOAuthClientSecret);
            setDestinationControlValue('overrideClientKey', destination.overrideClientKey);
            populateClientKeySelect('Destination', destination);
            setDestinationControlValue('clientKeyId', effectiveOverrideValue(destination.overrideClientKey, destination.clientKeyId, destination.hostDefaults.clientKeyId) || '0');
            setDestinationControlValue('overrideConnectTimeout', destination.overrideConnectTimeout);
            setDestinationControlValue('connectTimeoutSeconds', effectiveOverrideValue(destination.overrideConnectTimeout, destination.connectTimeoutSeconds, destination.hostDefaults.connectTimeoutSeconds));
            setDestinationControlValue('overrideDataTimeout', destination.overrideDataTimeout);
            setDestinationControlValue('dataTimeoutSeconds', effectiveOverrideValue(destination.overrideDataTimeout, destination.dataTimeoutSeconds, destination.hostDefaults.dataTimeoutSeconds));
            setDestinationControlValue('additionalCommandsBefore', destination.additionalCommandsBefore);
            setDestinationControlValue('additionalCommandsAfter', destination.additionalCommandsAfter);
            setDestinationControlValue('overrideTransferType', destination.overrideTransferType);
            setDestinationControlValue('transferType', effectiveOverrideValue(destination.overrideTransferType, destination.transferType, destination.hostDefaults.transferType) || 'binary');
            setDestinationControlValue('overridePassive', destination.overridePassive);
            setDestinationControlValue('passive', Boolean(effectiveOverrideValue(destination.overridePassive, destination.passive, destination.hostDefaults.passive)) ? 1 : 0);
            setDestinationControlValue('overrideXSha1', destination.overrideXSha1);
            setDestinationControlValue('useXSha1', Boolean(effectiveOverrideValue(destination.overrideXSha1, destination.useXSha1, destination.hostDefaults.useXSha1)));
            setDestinationControlValue('overrideBlindUpload', destination.overrideBlindUpload);
            setDestinationControlValue('blindUpload', Boolean(effectiveOverrideValue(destination.overrideBlindUpload, destination.blindUpload, destination.hostDefaults.blindUpload)));
            setDestinationControlValue('overrideResume', destination.overrideResume);
            setDestinationControlValue('resume', Boolean(effectiveOverrideValue(destination.overrideResume, destination.resume, destination.hostDefaults.resume)));
            setDestinationControlValue('overrideReuseTlsSession', destination.overrideReuseTlsSession);
            setDestinationControlValue('reuseTlsSession', Boolean(effectiveOverrideValue(destination.overrideReuseTlsSession, destination.reuseTlsSession, destination.hostDefaults.reuseTlsSession)));
            setDestinationControlValue('overrideFileAttributes', destination.overrideFileAttributes);
            setDestinationControlValue('setFileAttributes', Boolean(effectiveOverrideValue(destination.overrideFileAttributes, destination.setFileAttributes, destination.hostDefaults.setFileAttributes)));
            setDestinationControlValue('destinationFileAttributes', effectiveOverrideValue(destination.overrideFileAttributes, destination.destinationFileAttributes, destination.hostDefaults.destinationFileAttributes) || '');
            setDestinationControlValue('account', destination.account);
            refreshDestinationDependencies(destination);
        }

        function populateDestinationHostSelect(destination) {
            if (!destinationHostSelect) return;
            const step = activeStep();
            const selectedType = populateHostTypeFilter('Destination', destination, step?.key || '');
            destinationHostSelect.replaceChildren(new Option(selectedType ? 'Select a Host' : 'Select a Host Type first', ''));
            destinationHostSelect.options[0].disabled = Boolean(destination.hostId);
            filteredHosts('Destination', selectedType).forEach(host => {
                const option = new Option(host.name, host.id);
                option.dataset.hostTypeCode = host.typeCode;
                option.disabled = destination.serverHostTypeCode
                    ? !canChangeHost('Destination', destination.serverHostTypeCode, host.typeCode)
                    : !canWriteHostStep('Destination', host.typeCode);
                destinationHostSelect.appendChild(option);
            });
            if (destination.hostId && !Array.from(destinationHostSelect.options).some(option => option.value === destination.hostId)) {
                destinationHostSelect.appendChild(new Option(destination.hostName || `Host ID ${destination.hostId}`, destination.hostId));
            }
            destinationHostSelect.disabled = !selectedType;
            destinationHostSelect.value = destination.hostId || '';
            updateDestinationHostPresentation(destination);
            updateEditHostButtons();
        }

        function updateDestinationHostPresentation(destination) {
            const host = destinationHosts.find(item => item.id === destination.hostId);
            if (destinationHostHelp) {
                const parts = [host?.type || destination.hostType, host?.destination].filter(Boolean);
                if (destination.hostId && !canWriteHostStep('Destination', destination.hostTypeCode)) parts.push('Read-only in MAUI until this Host type has a verified write profile');
                destinationHostHelp.textContent = parts.join(' · ') || 'Select the MOVEit Host used by this Destination.';
            }
            if (destinationHostType) destinationHostType.textContent = `${destination.hostType || host?.type || 'Host'} Overrides`;
        }

        function refreshDestinationDependencies(destination) {
            if (destinationFileName) destinationFileName.hidden = destination.useOriginalName;
            const fileNameControl = dialog.querySelector('[data-destination-field="fileName"]');
            if (fileNameControl) fileNameControl.disabled = destination.useOriginalName;
            setDestinationOverrideFieldState('overrideRetryCount', 'retryCount', destination.hostDefaults.retryCount);
            setDestinationOverrideFieldState('overrideRetryTimeout', 'retryTimeoutSeconds', destination.hostDefaults.retryTimeoutSeconds);
            setDestinationOverrideFieldState('overrideWindowsCopyFileApi', 'useWindowsCopyFileApi', windowsCopyFileEnabled(destination.hostDefaults.useWindowsCopyFileApi));
            setDestinationOverrideFieldState('overridePreserveFileTimestamp', 'preserveFileTimestamp', destination.hostDefaults.preserveFileTimestamp);
            setDestinationOverrideFieldState('overrideConnectTimeout', 'connectTimeoutSeconds', destination.hostDefaults.connectTimeoutSeconds);
            setDestinationOverrideFieldState('overrideDataTimeout', 'dataTimeoutSeconds', destination.hostDefaults.dataTimeoutSeconds);
            setDestinationProtocolOverrideState('overrideTransferType', 'transferType', destination.hostDefaults.transferType);
            setDestinationProtocolOverrideState('overridePassive', 'passive', destination.hostDefaults.passive);
            setDestinationProtocolOverrideState('overrideXSha1', 'useXSha1', destination.hostDefaults.useXSha1);
            setDestinationProtocolOverrideState('overrideBlindUpload', 'blindUpload', destination.hostDefaults.blindUpload);
            setDestinationProtocolOverrideState('overrideResume', 'resume', destination.hostDefaults.resume);
            setDestinationProtocolOverrideState('overrideReuseTlsSession', 'reuseTlsSession', destination.hostDefaults.reuseTlsSession);
            setDependentControls('Destination', ['username', 'password', 'account'], destination.overrideCredentials);
            setDependentControls('Destination', ['sharePointAuthenticationMode', 'sharePointClientId', 'sharePointTenantId', 'sharePointRealmId', 'sharePointClientSecret', 'sharePointCertificateSha1', 'googleUseOAuth', 'googleProjectId', 'googleOAuthClientId', 'googleOAuthClientSecret', 'emailUseOAuth', 'emailOAuthType', 'emailOAuthUsername', 'emailOAuthClientId', 'emailOAuthClientSecret', 'emailOAuthTenantId', 'emailOAuthScope', 'emailOAuthTokenUrl'], destination.overrideCredentials);
            setDependentControls('Destination', ['clientCertificateSha1'], destination.overrideClientCertificate);
            setDependentControls('Destination', ['clientKeyId'], destination.overrideClientKey);
            setDestinationProtocolOverrideState('overrideFileAttributes', 'setFileAttributes', destination.hostDefaults.setFileAttributes);
            refreshDestinationPermissionMatrix(destination);
            const destinationProfile = hostWriteProfile('Destination', destination.hostTypeCode);
            const destinationIsS3 = destinationProfile === HOST_WRITE_PROFILE_S3;
            const destinationIsAzureBlob = destinationProfile === HOST_WRITE_PROFILE_AZURE_BLOB;
            const destinationIsSharePoint = destinationProfile === HOST_WRITE_PROFILE_SHAREPOINT;
            const destinationIsGoogleStorage = destinationProfile === HOST_WRITE_PROFILE_GOOGLE_STORAGE;
            const destinationIsSmtp = destinationProfile === HOST_WRITE_PROFILE_SMTP;
            const destinationEffectiveEmailOAuth = destinationIsSmtp && (destination.overrideCredentials ? Boolean(destination.emailUseOAuth) : destination.hostDefaults.emailUseOAuth === true);
            const destinationEffectiveEmailOAuthType = destination.overrideCredentials
                ? (destination.emailOAuthType || (destination.emailOAuthTenantId ? 'microsoft365' : 'custom'))
                : (destination.hostDefaults.emailOAuthTenantId ? 'microsoft365' : 'custom');
            const destinationEffectiveGoogleOAuth = destination.overrideCredentials ? Boolean(destination.googleUseOAuth) : destination.hostDefaults.googleUseOAuth === true;
            const destinationEffectiveSharePointMode = Number(destination.overrideCredentials ? (destination.sharePointAuthenticationMode ?? 2) : (destination.hostDefaults.sharePointAuthenticationMode ?? 2));
            if (destinationCredentialUserLabel) destinationCredentialUserLabel.textContent = destinationIsS3 || (destinationIsGoogleStorage && !destinationEffectiveGoogleOAuth) ? 'Access Key ID' : destinationIsAzureBlob ? 'Storage Account' : 'Username';
            if (destinationCredentialSecretLabel) destinationCredentialSecretLabel.textContent = destinationIsS3 || (destinationIsGoogleStorage && !destinationEffectiveGoogleOAuth) ? 'Secret Access Key' : destinationIsAzureBlob ? 'Key' : 'Password';
            const destinationGoogleHmac = dialog.querySelector('[data-traditional-task-editor-destination-google-hmac]');
            const destinationGoogleOAuth = dialog.querySelector('[data-traditional-task-editor-destination-google-oauth]');
            const destinationGoogleAuthHelp = dialog.querySelector('[data-traditional-task-editor-destination-google-auth-help]');
            if (destinationGoogleHmac) destinationGoogleHmac.checked = destinationIsGoogleStorage && !destinationEffectiveGoogleOAuth;
            if (destinationGoogleOAuth) destinationGoogleOAuth.checked = destinationIsGoogleStorage && destinationEffectiveGoogleOAuth;
            const destinationGenericCredentials = dialog.querySelector('[data-destination-generic-credentials]');
            if (destinationGenericCredentials) destinationGenericCredentials.hidden = (destinationIsGoogleStorage && destinationEffectiveGoogleOAuth) || destinationIsSharePoint || destinationEffectiveEmailOAuth;
            const destinationGoogleOAuthFields = dialog.querySelector('[data-destination-google-oauth-fields]');
            if (destinationGoogleOAuthFields) destinationGoogleOAuthFields.hidden = !(destinationIsGoogleStorage && destinationEffectiveGoogleOAuth);
            const destinationSharePointClientIdFields = dialog.querySelector('[data-destination-sharepoint-client-id-fields]');
            const destinationSharePointAppFields = dialog.querySelector('[data-destination-sharepoint-app-fields]');
            const destinationSharePointCertificateFields = dialog.querySelector('[data-destination-sharepoint-certificate-fields]');
            if (destinationSharePointClientIdFields) destinationSharePointClientIdFields.hidden = !(destinationIsSharePoint && [1, 2].includes(destinationEffectiveSharePointMode));
            if (destinationSharePointAppFields) destinationSharePointAppFields.hidden = !(destinationIsSharePoint && destinationEffectiveSharePointMode === 1);
            if (destinationSharePointCertificateFields) destinationSharePointCertificateFields.hidden = !(destinationIsSharePoint && destinationEffectiveSharePointMode === 2);
            const destinationGoogleSecretHelp = dialog.querySelector('[data-traditional-task-editor-destination-google-oauth-secret-help]');
            if (destinationGoogleSecretHelp) destinationGoogleSecretHelp.textContent = destination.googleOAuthClientSecretConfigured && !destination.googleOAuthClientSecret ? 'A Step private key is configured. Leave blank to keep it, or enter a replacement.' : 'Enter the Google OAuth private key for this Step override.';
            const destinationSharePointSecretHelp = dialog.querySelector('[data-traditional-task-editor-destination-sharepoint-client-secret-help]');
            if (destinationSharePointSecretHelp) destinationSharePointSecretHelp.textContent = destination.sharePointClientSecretConfigured && !destination.sharePointClientSecret ? 'A Step client secret is configured. Leave blank to keep it, or enter a replacement.' : 'Enter the SharePoint App Access client secret for this Step override.';
            if (destinationGoogleAuthHelp && destinationIsGoogleStorage) destinationGoogleAuthHelp.textContent = destination.overrideCredentials
                ? (destinationEffectiveGoogleOAuth ? 'Using a Task-level OAuth 2.0 override.' : 'Using a Task-level HMAC override.')
                : `Authentication is inherited from the selected Host (${destinationEffectiveGoogleOAuth ? 'OAuth 2.0' : 'HMAC'}).`;
            const destinationEmailLegacy = dialog.querySelector('[data-traditional-task-editor-destination-email-legacy]');
            const destinationEmailOAuth = dialog.querySelector('[data-traditional-task-editor-destination-email-oauth]');
            const destinationEmailOAuthDetails = dialog.querySelector('[data-traditional-task-editor-destination-email-oauth-details]');
            const destinationEmailAuthHelp = dialog.querySelector('[data-traditional-task-editor-destination-email-auth-help]');
            const destinationEmailOAuthSecretHelp = dialog.querySelector('[data-traditional-task-editor-destination-email-oauth-secret-help]');
            const destinationOverrideCredentialsControl = dialog.querySelector('[data-destination-field="overrideCredentials"]');
            if (destinationEmailLegacy) destinationEmailLegacy.checked = destinationIsSmtp && !destinationEffectiveEmailOAuth;
            if (destinationEmailOAuth) destinationEmailOAuth.checked = destinationIsSmtp && destinationEffectiveEmailOAuth;
            if (destinationEmailOAuthDetails) destinationEmailOAuthDetails.hidden = !(destinationIsSmtp && destinationEffectiveEmailOAuth);
            dialog.querySelectorAll('[data-destination-email-oauth-microsoft-only]').forEach(item => { item.hidden = !(destinationIsSmtp && destinationEffectiveEmailOAuth && destinationEffectiveEmailOAuthType === 'microsoft365'); });
            dialog.querySelectorAll('[data-destination-email-oauth-custom-only]').forEach(item => { item.hidden = !(destinationIsSmtp && destinationEffectiveEmailOAuth && destinationEffectiveEmailOAuthType === 'custom'); });
            if (destinationOverrideCredentialsControl && destinationIsSmtp) {
                destinationOverrideCredentialsControl.disabled = false;
                destinationOverrideCredentialsControl.title = '';
            }
            if (destinationEmailOAuthSecretHelp && destinationIsSmtp && destinationEffectiveEmailOAuth) {
                destinationEmailOAuthSecretHelp.textContent = destination.overrideCredentials
                    ? (destination.emailOAuthClientSecretConfigured && !destination.emailOAuthClientSecret ? 'A Step client secret is configured. Leave blank to keep it, or enter a replacement.' : 'Enter the OAuth client secret for this Step override.')
                    : 'Using the OAuth client secret configured on the SMTP Host.';
            }
            if (destinationEmailAuthHelp && destinationIsSmtp) destinationEmailAuthHelp.textContent = destination.overrideCredentials
                ? (destinationEffectiveEmailOAuth ? `Using a Task-level OAuth override (${destinationEffectiveEmailOAuthType === 'microsoft365' ? 'Microsoft Office 365' : 'Custom'}).` : 'Using a Task-level Legacy username/password override.')
                : `Authentication is inherited from the selected SMTP Host (${destinationEffectiveEmailOAuth ? 'OAuth' : 'Legacy'}).`;
            if (destinationBucketHelp) destinationBucketHelp.textContent = destination.useDefaultBucket
                ? (destination.hostDefaults.bucket ? `Using Host default bucket: ${destination.hostDefaults.bucket}` : 'Using the Host default bucket setting.')
                : 'This Bucket Name overrides the Host default for this Destination.';
            if (destinationContainerHelp) destinationContainerHelp.textContent = destination.useDefaultContainer
                ? (destination.hostDefaults.container ? `Using Host default container: ${destination.hostDefaults.container}` : 'Using the Host default container setting.')
                : 'This Container overrides the Host default for this Destination.';
            if (destinationDocumentLibraryHelp) destinationDocumentLibraryHelp.textContent = destination.useDefaultDocumentLibrary
                ? (destination.hostDefaults.documentLibrary ? `Using Host default library: ${destination.hostDefaults.documentLibrary}` : 'Using the Host default document library setting.')
                : 'This Document Library overrides the Host default for this Destination.';
            if (destinationPasswordHelp) destinationPasswordHelp.textContent = destination.overrideCredentials
                ? (destination.passwordConfigured && !destination.password ? 'A Step password is configured. Leave blank to keep it, or enter a replacement.' : 'Enter the password for this Step override.')
                : 'Using the Host default credentials.';
            if (destinationClientCertHelp) {
                const selected = tlsCertificates.find(item => item.sha1Thumbprint.replaceAll(' ', '').toLowerCase() === String(destination.clientCertificateSha1 || '').replaceAll(' ', '').toLowerCase());
                destinationClientCertHelp.textContent = destination.overrideClientCertificate
                    ? (selected ? `${selected.name}${selected.expirationDate ? ` · expires ${selected.expirationDate}` : ''}` : (destination.clientCertificateSubject || 'No client certificate selected.'))
                    : (destination.hostDefaults.clientCertificateSubject ? `Using Host default: ${destination.hostDefaults.clientCertificateSubject}` : 'Using the Host default client certificate setting.');
            }
            if (destinationClientKeyHelp) {
                const effectiveKeyId = String(effectiveOverrideValue(destination.overrideClientKey, destination.clientKeyId, destination.hostDefaults.clientKeyId) || '0');
                const selectedKey = sshClientKeys.find(item => String(item.id) === effectiveKeyId);
                destinationClientKeyHelp.textContent = destination.overrideClientKey
                    ? (selectedKey ? selectedKey.name : (effectiveKeyId === '0' ? 'No client key will be used for this Destination override.' : `SSH Client Key ID ${effectiveKeyId}`))
                    : (selectedKey ? `Using Host default: ${selectedKey.name}` : (effectiveKeyId === '0' ? 'Using the Host default client key setting.' : `Using Host default SSH Client Key ID ${effectiveKeyId}`));
            }
            const destinationEffectiveResume = Boolean(effectiveOverrideValue(destination.overrideResume, destination.resume, destination.hostDefaults.resume));
            const destinationEffectiveRetryCount = Number(effectiveOverrideValue(destination.overrideRetryCount, destination.retryCount, destination.hostDefaults.retryCount) ?? 0);
            dialog.querySelectorAll('[data-traditional-task-editor-destination-sftp-resume-retry-warning]').forEach(item => {
                item.hidden = !(destinationEffectiveResume && destinationEffectiveRetryCount === 0);
            });
            const destinationAccountHelp = dialog.querySelector('[data-traditional-task-editor-destination-account-help]');
            if (destinationAccountHelp) destinationAccountHelp.textContent = destination.overrideCredentials
                ? (destination.accountConfigured && !destination.account ? 'A Step account is configured. Leave blank to keep it, or enter a replacement.' : 'Optional; only FTP servers that require an account value use this setting.')
                : 'Using the Host default account setting.';
            const destinationAppendOption = dialog.querySelector('[data-destination-append-option]');
            if (destinationAppendOption) {
                const cloudAppendUnavailable = [HOST_WRITE_PROFILE_S3, HOST_WRITE_PROFILE_AZURE_BLOB, HOST_WRITE_PROFILE_SHAREPOINT, HOST_WRITE_PROFILE_GOOGLE_STORAGE, HOST_WRITE_PROFILE_SMTP].includes(destinationProfile);
                destinationAppendOption.hidden = cloudAppendUnavailable && Number(destination.overwriteAction || 0) !== 2;
            }

            configureDestinationHostOverrideTabs(destination);
            updateDestinationHostPresentation(destination);
            updateFolderBrowseAvailability('Destination', destination, destinationBrowseButton, destinationBrowseWarning);
            const step = activeStep();
            if (step) configureStepActions(step);
            applyHostStepWriteAvailability('Destination', destination);
        }

        function setDestinationOverrideFieldState(toggleField, valueField, defaultValue) {
            const toggle = dialog.querySelector(`[data-destination-field="${toggleField}"]`);
            const override = Boolean(toggle?.checked);
            dialog.querySelectorAll(`[data-destination-field="${valueField}"]`).forEach(value => {
                value.disabled = !override;
                value.title = override ? '' : defaultValue == null ? 'Using the Host default.' : `Using Host default: ${formatValue(defaultValue)}`;
            });
        }

        function setDestinationProtocolOverrideState(toggleField, valueField, defaultValue) {
            const toggle = dialog.querySelector(`[data-destination-field="${toggleField}"]`);
            const override = Boolean(toggle?.checked);
            dialog.querySelectorAll(`[data-destination-field="${valueField}"]`).forEach(value => {
                value.disabled = !override;
                value.title = override ? '' : defaultValue == null ? 'Using the Host default.' : `Using Host default: ${formatValue(defaultValue)}`;
            });
        }

        function configureDestinationHostOverrideTabs(destination) {
            const typeCode = String(destination.hostTypeCode || '').trim();
            const writeProfile = hostWriteProfile('Destination', typeCode);
            const isUnc = writeProfile === HOST_WRITE_PROFILE_SHARE;
            const isLocal = writeProfile === HOST_WRITE_PROFILE_LOCAL;
            const isMoveItTransfer = writeProfile === HOST_WRITE_PROFILE_MOVEIT_TRANSFER;
            const isFtp = writeProfile === HOST_WRITE_PROFILE_FTP;
            const isSftp = writeProfile === HOST_WRITE_PROFILE_SFTP;
            const isS3 = writeProfile === HOST_WRITE_PROFILE_S3;
            const isAzureBlob = writeProfile === HOST_WRITE_PROFILE_AZURE_BLOB;
            const isSharePoint = writeProfile === HOST_WRITE_PROFILE_SHAREPOINT;
            const isGoogleStorage = writeProfile === HOST_WRITE_PROFILE_GOOGLE_STORAGE;
            const isSmtp = writeProfile === HOST_WRITE_PROFILE_SMTP;
            const supported = canWriteHostStep('Destination', typeCode);
            if (destinationHostOverrideSupported) destinationHostOverrideSupported.hidden = !supported;
            if (destinationHostOverrideUnsupported) {
                destinationHostOverrideUnsupported.hidden = supported;
                if (!supported) destinationHostOverrideUnsupported.textContent = typeCode
                    ? `${destination.hostType || destination.hostTypeCode} Destination overrides do not yet have a verified MAUI write profile. Existing task values are preserved.`
                    : 'Select a Host to display the settings supported for that Host type.';
            }
            destinationTabs.forEach(tab => {
                if (tab.dataset.traditionalTaskEditorDestinationTab === 'additional-commands') tab.hidden = !isFtp;
            });
            if (destinationTabName === 'additional-commands' && !isFtp) setDestinationTab('general');
            destinationHostTabs.forEach(tab => {
                const name = tab.dataset.traditionalTaskEditorDestinationHostTab;
                if (name === 'authentication') tab.hidden = !(isMoveItTransfer || isFtp || isSftp || isS3 || isAzureBlob || isSharePoint || isGoogleStorage || isSmtp);
                if (name === 'transfer') tab.hidden = !(isLocal || isUnc || isFtp || isSftp);
                if (name === 'file-attributes') tab.hidden = !isSftp;
                if (name === 'timeouts') tab.hidden = false;
            });
            dialog.querySelectorAll('[data-destination-connect-timeout-only]').forEach(item => { item.hidden = !(isMoveItTransfer || isFtp || isSftp || isS3 || isAzureBlob || isSharePoint || isGoogleStorage || isSmtp); });
            dialog.querySelectorAll('[data-destination-data-timeout-only]').forEach(item => { item.hidden = !(isMoveItTransfer || isFtp || isS3 || isAzureBlob || isSharePoint || isGoogleStorage || isSmtp); });
            dialog.querySelectorAll('[data-destination-unc-only]').forEach(item => { item.hidden = !isUnc; });
            dialog.querySelectorAll('[data-destination-file-transfer-only]').forEach(item => { item.hidden = !(isLocal || isUnc); });
            dialog.querySelectorAll('[data-destination-ftp-only]').forEach(item => { item.hidden = !isFtp; });
            dialog.querySelectorAll('[data-destination-sftp-only]').forEach(item => { item.hidden = !isSftp; });
            dialog.querySelectorAll('[data-destination-s3-general-only]').forEach(item => { item.hidden = !(isS3 || isGoogleStorage); });
            dialog.querySelectorAll('[data-destination-azureblob-general-only]').forEach(item => { item.hidden = !isAzureBlob; });
            dialog.querySelectorAll('[data-destination-sharepoint-general-only]').forEach(item => { item.hidden = !isSharePoint; });
            dialog.querySelectorAll('[data-destination-sharepoint-auth-only]').forEach(item => { item.hidden = !isSharePoint; });
            dialog.querySelectorAll('[data-destination-google-auth-only]').forEach(item => { item.hidden = !isGoogleStorage; });
            dialog.querySelectorAll('[data-destination-email-auth-only]').forEach(item => { item.hidden = !isSmtp; });
            dialog.querySelectorAll('[data-destination-smtp-only]').forEach(item => { item.hidden = !isSmtp; });
            dialog.querySelectorAll('[data-destination-non-smtp]').forEach(item => { item.hidden = isSmtp; });
            dialog.querySelectorAll('[data-destination-credential-override-toggle]').forEach(item => { item.hidden = false; });
            if (destinationBucketBrowseButton) destinationBucketBrowseButton.disabled = !(isS3 || isGoogleStorage) || !destination.hostId || !canBrowseHostStep('Destination', destination.hostTypeCode);
            if (destinationContainerBrowseButton) destinationContainerBrowseButton.disabled = !isAzureBlob || !destination.hostId || !canBrowseHostStep('Destination', destination.hostTypeCode);
            if (destinationDocumentLibraryBrowseButton) destinationDocumentLibraryBrowseButton.disabled = !isSharePoint || !destination.hostId || !canBrowseHostStep('Destination', destination.hostTypeCode);
            dialog.querySelectorAll('[data-destination-cloud-force-directory]').forEach(item => { item.hidden = isS3 || isAzureBlob || isGoogleStorage || isSmtp; });
            dialog.querySelectorAll('[data-destination-cert-only]').forEach(item => { item.hidden = !(isMoveItTransfer || isFtp); });
            const visibleNames = destinationHostTabs.filter(tab => !tab.hidden).map(tab => tab.dataset.traditionalTaskEditorDestinationHostTab);
            if (!visibleNames.includes(destinationHostTabName)) destinationHostTabName = visibleNames[0] || 'timeouts';
            setDestinationHostTab(destinationHostTabName);
        }

        function setDestinationControlValue(field, value) {
            const controls = Array.from(dialog.querySelectorAll(`[data-destination-field="${field}"]`));
            if (controls.length === 0) return;
            if (controls[0] instanceof HTMLInputElement && controls[0].type === 'radio') {
                controls.forEach(control => { control.checked = String(control.value) === String(value); });
                return;
            }
            const control = controls[0];
            if (control instanceof HTMLInputElement && control.type === 'checkbox') controls.forEach(item => { item.checked = Boolean(value); });
            else control.value = value == null ? '' : String(value);
        }

        function destinationControlValue(control) {
            if (control instanceof HTMLInputElement && control.type === 'checkbox' && control.dataset.destinationField === 'useWindowsCopyFileApi') return !control.checked;
            if (control instanceof HTMLInputElement && control.type === 'checkbox') return control.checked;
            if (control instanceof HTMLInputElement && control.type === 'radio') {
                if (control.dataset.destinationField === 'transferType') return String(control.value || '').toLowerCase();
                if (control.dataset.destinationField === 'passive') return String(control.value) === '1';
                if (control.dataset.destinationField === 'isHtml') return String(control.value) === '1';
                if (control.dataset.destinationField === 'emailUseOAuth') return String(control.value) === '1';
                return Number(control.value);
            }
            if (control instanceof HTMLInputElement && control.type === 'number') return control.value === '' ? null : Number(control.value);
            return String(control?.value ?? '');
        }

        function normalizedDestinationPermissionValue(value) {
            const text = String(value ?? '').trim();
            return /^[0-7]{3}$/.test(text) ? text : '';
        }

        function destinationPermissionSymbolic(value) {
            const normalized = normalizedDestinationPermissionValue(value);
            if (!normalized) return '';
            return [...normalized].map(character => {
                const digit = Number(character);
                return `${digit & 4 ? 'r' : '-'}${digit & 2 ? 'w' : '-'}${digit & 1 ? 'x' : '-'}`;
            }).join('');
        }

        function destinationPermissionValueFromMatrix() {
            const digits = [0, 0, 0];
            dialog.querySelectorAll('[data-destination-permission]').forEach(control => {
                if (!(control instanceof HTMLInputElement) || control.type !== 'checkbox' || !control.checked) return;
                const column = Number(control.dataset.permissionColumn);
                const bit = Number(control.dataset.permissionBit);
                if (Number.isInteger(column) && column >= 0 && column < digits.length && [1, 2, 4].includes(bit)) digits[column] |= bit;
            });
            return digits.join('');
        }

        function refreshDestinationPermissionMatrix(destination) {
            const effectiveValue = normalizedDestinationPermissionValue(
                effectiveOverrideValue(destination.overrideFileAttributes, destination.destinationFileAttributes, destination.hostDefaults.destinationFileAttributes));
            const digits = effectiveValue ? [...effectiveValue].map(Number) : [0, 0, 0];
            const enabled = Boolean(destination.overrideFileAttributes && destination.setFileAttributes);

            dialog.querySelectorAll('[data-destination-permission]').forEach(control => {
                if (!(control instanceof HTMLInputElement) || control.type !== 'checkbox') return;
                const column = Number(control.dataset.permissionColumn);
                const bit = Number(control.dataset.permissionBit);
                control.checked = Boolean(effectiveValue && Number.isInteger(column) && column >= 0 && column < digits.length && (digits[column] & bit));
                control.disabled = !enabled;
            });

            const summary = dialog.querySelector('[data-destination-file-permission-summary]');
            if (summary) summary.textContent = effectiveValue ? `${destinationPermissionSymbolic(effectiveValue)} (${effectiveValue})` : 'Not set';

            const help = dialog.querySelector('[data-destination-file-permission-help]');
            if (help) {
                if (!destination.overrideFileAttributes) help.textContent = effectiveValue ? `Using Host default permissions ${effectiveValue}.` : 'Using the Host default file-attribute setting.';
                else if (!destination.setFileAttributes) help.textContent = 'File permissions will not be changed after upload.';
                else if (!effectiveValue) help.textContent = 'Select the User, Group, and Others permissions to apply after upload.';
                else help.textContent = `MOVEit will store these permissions as ${effectiveValue}.`;
            }

            const hiddenValue = dialog.querySelector('[data-destination-field="destinationFileAttributes"]');
            if (hiddenValue) {
                hiddenValue.value = effectiveValue;
                hiddenValue.disabled = !enabled;
            }
        }

        function applyDestinationPermissionControl(control) {
            const step = activeStep();
            const destination = step?.destination;
            if (!step || !destination || control?.disabled || !destination.overrideFileAttributes || !destination.setFileAttributes) return;

            const field = 'destinationFileAttributes';
            const stableKey = destinationStableKey(step.key, field);
            const before = destinationStableValues.has(stableKey) ? destinationStableValues.get(stableKey) : cloneSimple(destination[field]);
            destination[field] = destinationPermissionValueFromMatrix();

            if (!sameValue(before, destination[field])) {
                lastUndo = { kind: 'destination-field', stepKey: step.key, field, before, label: destinationFieldLabel(field) };
                seedDestinationStableValues(step);
                updateUndoState();
            }

            refreshDestinationPermissionMatrix(destination);
            updateStepSummary(step);
            updateDirtyState();
        }

        function destinationStableKey(stepKey, field) { return `${stepKey}|${field}`; }

        function seedDestinationStableValues(step) {
            if (!step?.destination) return;
            Object.keys(step.destination).forEach(field => {
                if (field === 'hostDefaults') return;
                destinationStableValues.set(destinationStableKey(step.key, field), cloneSimple(step.destination[field]));
            });
        }

        function applyDestinationControl(control, commit) {
            const step = activeStep();
            const destination = step?.destination;
            const field = control?.dataset?.destinationField;
            if (!step || !destination || !field) return;
            const after = destinationControlValue(control);
            const stableKey = destinationStableKey(step.key, field);
            const before = destinationStableValues.has(stableKey) ? destinationStableValues.get(stableKey) : cloneSimple(destination[field]);
            const snapshotFields = new Set([
                'hostId', 'overrideRetryCount', 'overrideRetryTimeout', 'overrideWindowsCopyFileApi', 'overridePreserveFileTimestamp',
                'overrideCredentials', 'overrideClientCertificate', 'overrideClientKey', 'overrideConnectTimeout', 'overrideDataTimeout',
                'overrideTransferType', 'overridePassive', 'overrideXSha1', 'overrideBlindUpload',
                'overrideResume', 'overrideReuseTlsSession', 'overrideFileAttributes', 'setFileAttributes', 'emailUseOAuth', 'emailOAuthType'
            ]);
            const beforeDestination = snapshotFields.has(field) ? deepClone(destination) : null;
            applyDestinationValue(destination, field, after);
            if (commit && !sameValue(before, destination[field])) {
                lastUndo = beforeDestination
                    ? { kind: 'destination-snapshot', stepKey: step.key, before: beforeDestination, label: destinationFieldLabel(field) }
                    : { kind: 'destination-field', stepKey: step.key, field, before, label: destinationFieldLabel(field) };
                seedDestinationStableValues(step);
                updateUndoState();
            }
            updateStepSummary(step);
            refreshDestinationDependencies(destination);
            updateDirtyState();
        }

        function resetDestinationHostSpecificState(destination) {
            destination.path = '';
            destination.useDefaultBucket = true;
            destination.bucket = destination.hostDefaults.bucket || '';
            destination.useDefaultContainer = true;
            destination.container = destination.hostDefaults.container || '';
            destination.useDefaultDocumentLibrary = true;
            destination.documentLibrary = destination.hostDefaults.documentLibrary || '';
            destination.documentLibraryGuid = destination.hostDefaults.documentLibraryGuid || '';
            destination.requiresFolderSelection = Boolean(destination.hostId) && hostWriteProfile('Destination', destination.hostTypeCode) !== HOST_WRITE_PROFILE_SMTP;
            destination.overrideRetryCount = false;
            destination.retryCount = destination.hostDefaults.retryCount;
            destination.overrideRetryTimeout = false;
            destination.retryTimeoutSeconds = destination.hostDefaults.retryTimeoutSeconds;
            destination.overrideWindowsCopyFileApi = false;
            destination.useWindowsCopyFileApi = destination.hostDefaults.useWindowsCopyFileApi;
            destination.overridePreserveFileTimestamp = false;
            destination.preserveFileTimestamp = destination.hostDefaults.preserveFileTimestamp;
            destination.overrideCredentials = false;
            destination.username = destination.hostDefaults.username;
            destination.passwordConfigured = false;
            destination.password = '';
            destination.overrideClientCertificate = false;
            destination.clientCertificateSha1 = destination.hostDefaults.clientCertificateSha1;
            destination.clientCertificateStore = destination.hostDefaults.clientCertificateStore;
            destination.clientCertificateSubject = destination.hostDefaults.clientCertificateSubject;
            destination.sharePointCertificateSha1 = destination.hostDefaults.clientCertificateSha1 || '';
            destination.sharePointAuthenticationMode = destination.hostDefaults.sharePointAuthenticationMode ?? 2;
            destination.sharePointClientId = destination.hostDefaults.sharePointClientId || '';
            destination.sharePointTenantId = destination.hostDefaults.sharePointTenantId || '';
            destination.sharePointRealmId = destination.hostDefaults.sharePointRealmId || '';
            destination.sharePointClientSecretConfigured = false;
            destination.sharePointClientSecret = '';
            destination.googleUseOAuth = destination.hostDefaults.googleUseOAuth ?? false;
            destination.googleProjectId = destination.hostDefaults.googleProjectId || '';
            destination.googleOAuthClientId = destination.hostDefaults.googleOAuthClientId || '';
            destination.googleOAuthClientSecretConfigured = false;
            destination.googleOAuthClientSecret = '';
            destination.emailUseOAuth = destination.hostDefaults.emailUseOAuth ?? false;
            destination.emailOAuthType = destination.hostDefaults.emailOAuthTenantId ? 'microsoft365' : 'custom';
            destination.emailOAuthUsername = destination.hostDefaults.username || '';
            destination.emailOAuthClientId = destination.hostDefaults.emailOAuthClientId || '';
            destination.emailOAuthScope = destination.hostDefaults.emailOAuthScope || '';
            destination.emailOAuthTokenUrl = destination.hostDefaults.emailOAuthTokenUrl || '';
            destination.emailOAuthTenantId = destination.hostDefaults.emailOAuthTenantId || '';
            destination.emailOAuthClientSecretConfigured = false;
            destination.emailOAuthClientSecret = '';
            destination.overrideClientKey = false;
            destination.clientKeyId = destination.hostDefaults.clientKeyId || '0';
            destination.overrideConnectTimeout = false;
            destination.connectTimeoutSeconds = destination.hostDefaults.connectTimeoutSeconds;
            destination.overrideDataTimeout = false;
            destination.dataTimeoutSeconds = destination.hostDefaults.dataTimeoutSeconds;
            destination.additionalCommandsBefore = '';
            destination.additionalCommandsAfter = '';
            destination.overrideTransferType = false;
            destination.transferType = destination.hostDefaults.transferType || 'binary';
            destination.overridePassive = false;
            destination.passive = destination.hostDefaults.passive;
            destination.overrideXSha1 = false;
            destination.useXSha1 = destination.hostDefaults.useXSha1;
            destination.overrideBlindUpload = false;
            destination.blindUpload = destination.hostDefaults.blindUpload;
            destination.overrideResume = false;
            destination.resume = destination.hostDefaults.resume;
            destination.overrideReuseTlsSession = false;
            destination.reuseTlsSession = destination.hostDefaults.reuseTlsSession;
            destination.overrideFileAttributes = false;
            destination.setFileAttributes = destination.hostDefaults.setFileAttributes;
            destination.destinationFileAttributes = destination.hostDefaults.destinationFileAttributes || '';
            destination.account = '';
            destination.accountConfigured = false;
        }

        function applyDestinationValue(destination, field, value) {
            const previousHostId = String(destination.hostId || '');
            destination[field] = value;
            if (field === 'hostId') {
                const host = destinationHosts.find(item => item.id === String(value));
                destination.hostId = String(value || '');
                destination.hostName = host?.name || '';
                destination.hostTypeCode = host?.typeCode || '';
                destination.hostType = host?.type || '';
                destination.hostDefaults = normalizeHostDefaults(host?.defaults || {});
                if (host?.typeCode) destinationHostTypeFilters.set(activeStep()?.key || '', host.typeCode);
                if (previousHostId !== destination.hostId) resetDestinationHostSpecificState(destination);
                populateDestinationHostSelect(destination);
            }
            if (field === 'path' && String(destination.path || '').trim()) destination.requiresFolderSelection = false;
            if (field === 'bucket') {
                const nextBucket = String(value || '').trim();
                destination.useDefaultBucket = nextBucket === String(destination.hostDefaults.bucket || '').trim();
                destination.bucket = nextBucket;
            }
            if (field === 'container') {
                const nextContainer = String(value || '').trim();
                destination.useDefaultContainer = nextContainer === String(destination.hostDefaults.container || '').trim();
                destination.container = nextContainer;
            }
            if (field === 'documentLibrary') {
                const nextLibrary = String(value || '').trim();
                destination.useDefaultDocumentLibrary = nextLibrary === String(destination.hostDefaults.documentLibrary || '').trim()
                    && String(destination.documentLibraryGuid || '').trim() === String(destination.hostDefaults.documentLibraryGuid || '').trim();
                destination.documentLibrary = nextLibrary;
            }
            if (field === 'overrideRetryCount' && value && destination.retryCount == null) destination.retryCount = destination.hostDefaults.retryCount;
            if (field === 'overrideRetryTimeout' && value && destination.retryTimeoutSeconds == null) destination.retryTimeoutSeconds = destination.hostDefaults.retryTimeoutSeconds;
            if (field === 'overrideWindowsCopyFileApi' && value && destination.useWindowsCopyFileApi == null) destination.useWindowsCopyFileApi = destination.hostDefaults.useWindowsCopyFileApi;
            if (field === 'overridePreserveFileTimestamp' && value && destination.preserveFileTimestamp == null) destination.preserveFileTimestamp = destination.hostDefaults.preserveFileTimestamp;
            if (field === 'overrideCredentials' && value && !String(destination.username || '').trim()) destination.username = destination.hostDefaults.username;
            if (field === 'overrideCredentials' && value && hostWriteProfile('Destination', destination.hostTypeCode) === HOST_WRITE_PROFILE_SMTP) {
                if (destination.emailUseOAuth == null) destination.emailUseOAuth = destination.hostDefaults.emailUseOAuth ?? false;
                if (!String(destination.emailOAuthUsername || '').trim()) destination.emailOAuthUsername = destination.hostDefaults.username || '';
                if (!String(destination.emailOAuthClientId || '').trim()) destination.emailOAuthClientId = destination.hostDefaults.emailOAuthClientId || '';
                if (!String(destination.emailOAuthScope || '').trim()) destination.emailOAuthScope = destination.hostDefaults.emailOAuthScope || '';
                if (!String(destination.emailOAuthTokenUrl || '').trim()) destination.emailOAuthTokenUrl = destination.hostDefaults.emailOAuthTokenUrl || '';
                if (!String(destination.emailOAuthTenantId || '').trim()) destination.emailOAuthTenantId = destination.hostDefaults.emailOAuthTenantId || '';
                if (!String(destination.emailOAuthType || '').trim()) destination.emailOAuthType = destination.emailOAuthTenantId ? 'microsoft365' : 'custom';
            }
            if (field === 'emailUseOAuth' && value) {
                if (!String(destination.emailOAuthUsername || '').trim()) destination.emailOAuthUsername = destination.hostDefaults.username || destination.username || '';
                if (!String(destination.emailOAuthClientId || '').trim()) destination.emailOAuthClientId = destination.hostDefaults.emailOAuthClientId || '';
                if (!String(destination.emailOAuthTenantId || '').trim()) destination.emailOAuthTenantId = destination.hostDefaults.emailOAuthTenantId || '';
                if (!String(destination.emailOAuthScope || '').trim()) destination.emailOAuthScope = destination.hostDefaults.emailOAuthScope || 'https://outlook.office365.com/.default';
                if (!String(destination.emailOAuthTokenUrl || '').trim()) destination.emailOAuthTokenUrl = destination.hostDefaults.emailOAuthTokenUrl || '';
                if (!String(destination.emailOAuthType || '').trim()) destination.emailOAuthType = destination.emailOAuthTenantId ? 'microsoft365' : 'custom';
            }
            if (field === 'emailOAuthType' && value === 'microsoft365' && !String(destination.emailOAuthScope || '').trim()) destination.emailOAuthScope = 'https://outlook.office365.com/.default';
            if (field === 'overrideTransferType' && value && !String(destination.transferType || '').trim()) destination.transferType = destination.hostDefaults.transferType || 'binary';
            if (field === 'overridePassive' && value && destination.passive == null) destination.passive = destination.hostDefaults.passive ?? true;
            if (field === 'overrideXSha1' && value && destination.useXSha1 == null) destination.useXSha1 = destination.hostDefaults.useXSha1 ?? false;
            if (field === 'overrideBlindUpload' && value && destination.blindUpload == null) destination.blindUpload = destination.hostDefaults.blindUpload ?? false;
            if (field === 'overrideResume' && value && destination.resume == null) destination.resume = destination.hostDefaults.resume ?? false;
            if (field === 'overrideReuseTlsSession' && value && destination.reuseTlsSession == null) destination.reuseTlsSession = destination.hostDefaults.reuseTlsSession ?? false;
            if (field === 'overrideClientKey' && value && !String(destination.clientKeyId || '').trim()) destination.clientKeyId = destination.hostDefaults.clientKeyId || '0';
            if (field === 'overrideFileAttributes' && value) {
                if (destination.setFileAttributes == null) destination.setFileAttributes = destination.hostDefaults.setFileAttributes ?? false;
                if (!String(destination.destinationFileAttributes || '').trim()) destination.destinationFileAttributes = destination.hostDefaults.destinationFileAttributes || '';
            }
            if (field === 'setFileAttributes' && value && !String(destination.destinationFileAttributes || '').trim()) destination.destinationFileAttributes = destination.hostDefaults.destinationFileAttributes || '';
            if (field === 'overrideClientCertificate' && value && !String(destination.clientCertificateSha1 || '').trim()) {
                destination.clientCertificateSha1 = destination.hostDefaults.clientCertificateSha1;
                destination.clientCertificateStore = destination.hostDefaults.clientCertificateStore;
                destination.clientCertificateSubject = destination.hostDefaults.clientCertificateSubject;
            }
            if (field === 'clientCertificateSha1') {
                const certificate = tlsCertificates.find(item => item.sha1Thumbprint.replaceAll(' ', '').toLowerCase() === String(value || '').replaceAll(' ', '').toLowerCase());
                destination.clientCertificateStore = certificate?.store || (value ? destination.clientCertificateStore || 'My' : '');
                destination.clientCertificateSubject = certificate?.name || (value ? destination.clientCertificateSubject : '');
            }
            if (field === 'sharePointCertificateSha1') {
                const certificate = tlsCertificates.find(item => item.sha1Thumbprint.replaceAll(' ', '').toLowerCase() === String(value || '').replaceAll(' ', '').toLowerCase());
                destination.sharePointCertificateSha1 = String(value || '');
                destination.clientCertificateSha1 = String(value || '');
                destination.clientCertificateStore = certificate?.store || (value ? destination.clientCertificateStore || 'My' : '');
                destination.clientCertificateSubject = certificate?.name || (value ? destination.clientCertificateSubject : '');
            }
            if (field === 'overrideConnectTimeout' && value && destination.connectTimeoutSeconds == null) destination.connectTimeoutSeconds = destination.hostDefaults.connectTimeoutSeconds;
            if (field === 'overrideDataTimeout' && value && destination.dataTimeoutSeconds == null) destination.dataTimeoutSeconds = destination.hostDefaults.dataTimeoutSeconds;
            if (field.startsWith('override') || field === 'hostId' || field === 'clientCertificateSha1' || field === 'sharePointCertificateSha1' || field === 'clientKeyId' || field === 'setFileAttributes' || field === 'bucket' || field === 'container' || field === 'documentLibrary' || field.startsWith('sharePoint') || field === 'emailUseOAuth' || field.startsWith('emailOAuth')) renderDestination(activeStep());
        }

        // SMTP and POP3 Task overrides share the captured native mail credential contract.
        // Omit untouched secrets so the shared save layer retains MOVEit's protected values.
        function applyMailCredentialPatch(patch, mail) {
            if (!mail.overrideCredentials) return;
            const useOAuth = Boolean(mail.emailUseOAuth);
            patch.UseOAuth = useOAuth ? 1 : 0;
            if (useOAuth) {
                patch.Username = String(mail.emailOAuthUsername || '');
                patch.OAuthClientID = String(mail.emailOAuthClientId || '');
                if (String(mail.emailOAuthType || 'microsoft365').toLowerCase() === 'microsoft365') {
                    const tenantId = String(mail.emailOAuthTenantId || '').trim();
                    patch.OAuthScope = 'https://outlook.office365.com/.default';
                    patch.OAuthTokenURL = `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/token`;
                } else {
                    patch.OAuthScope = String(mail.emailOAuthScope || '');
                    patch.OAuthTokenURL = String(mail.emailOAuthTokenUrl || '');
                }
                if (String(mail.emailOAuthClientSecret || '').length) patch.OAuthClientSecret = String(mail.emailOAuthClientSecret);
            } else {
                patch.Username = String(mail.username || '');
                if (String(mail.password || '').length) patch.Password = String(mail.password);
            }
        }

        function destinationFieldLabel(field) {
            return ({
                hostId: 'Destination Host', bucket: 'Bucket Name', container: 'Container', documentLibrary: 'Document Library', path: 'Destination Folder', useRelativeSubdirectories: 'Use Relative Subdirectories', forceDirectory: 'Create Directories',
                zip: 'Compress Each File', useOriginalName: 'Use Original Filename', fileName: 'Destination File Name', overwriteAction: 'Existing File Action',
                overrideRetryCount: 'Retry Count Override', retryCount: 'Retry Count', overrideRetryTimeout: 'Retry Timeout Override', retryTimeoutSeconds: 'Retry Timeout',
                overrideWindowsCopyFileApi: 'Windows CopyFile API Override', useWindowsCopyFileApi: 'Windows CopyFile API',
                overridePreserveFileTimestamp: 'Preserve File Timestamp Override', preserveFileTimestamp: 'Preserve File Timestamp on Upload',
                overrideCredentials: 'Credentials Override', username: 'Username', password: 'Password', account: 'FTP Account', emailUseOAuth: 'SMTP Authentication', emailOAuthType: 'SMTP OAuth Type', emailOAuthUsername: 'SMTP OAuth Username', emailOAuthClientId: 'SMTP OAuth Client ID', emailOAuthClientSecret: 'SMTP OAuth Client Secret', emailOAuthTenantId: 'SMTP OAuth Tenant ID', emailOAuthScope: 'SMTP OAuth Authorization Scope', emailOAuthTokenUrl: 'SMTP OAuth Token URL', sharePointAuthenticationMode: 'SharePoint Authentication', sharePointClientId: 'SharePoint Client ID', sharePointTenantId: 'SharePoint Tenant ID', sharePointCertificateSha1: 'SharePoint Certificate',
                overrideClientCertificate: 'Client Certificate Override', clientCertificateSha1: 'Client Certificate',
                overrideConnectTimeout: 'Connect Timeout Override', connectTimeoutSeconds: 'Connect Timeout',
                overrideDataTimeout: 'Data Timeout Override', dataTimeoutSeconds: 'Data Timeout',
                additionalCommandsBefore: 'FTP Commands Before Transfer', additionalCommandsAfter: 'FTP Commands After Transfer',
                overrideTransferType: 'FTP Transfer Type Override', transferType: 'FTP Transfer Type',
                overridePassive: 'FTP Transfer Mode Override', passive: 'FTP Transfer Mode',
                overrideXSha1: 'FTP XSHA1 Override', useXSha1: 'FTP XSHA1',
                overrideBlindUpload: 'FTP Blind Upload Override', blindUpload: 'FTP Blind Upload',
                overrideResume: 'FTP Resume Override', resume: 'FTP Resume',
                overrideReuseTlsSession: 'FTP Reuse TLS Session Override', reuseTlsSession: 'FTP Reuse TLS Session',
                overrideFileAttributes: 'File Attributes Override', setFileAttributes: 'Set File Permissions', destinationFileAttributes: 'File Permissions'
            })[field] || field;
        }


        function updateStepSummary(step) {
            if (!step) return;
            if ((step.kind === 'Source' && step.source) || (step.kind === 'Process' && step.process) || (step.kind === 'Destination' && step.destination)) step.subtitle = stepSummaryText(step);
            if (stepSummary && step.key === activeStepKey) stepSummary.textContent = stepSummaryText(step);
            renderStepRail();
        }

        // Produces the documented MOVEit REST fields controlled by the RC2 Step editors.
        // This is a PATCH contract, not a replacement task document: when Save is enabled the server
        // will apply these values to a fresh MOVEit Task definition so unknown/version-specific fields,
        // Supported Source Special Filters are written explicitly; unrecognized Criteria and unchanged secure values are preserved losslessly.
        function buildMoveItStepPatches() {
            const flag = value => value ? 1 : 0;
            const withId = (target, id) => {
                const value = String(id || '').trim();
                if (value) target.ID = value;
                return target;
            };
            const optionalNumber = (target, name, value, enabled = true) => {
                if (enabled && value != null && Number.isFinite(Number(value))) target[name] = Number(value);
            };
            const parameterPatch = parameters => ({
                replace: true,
                any: (Array.isArray(parameters) ? parameters : []).map(parameter => parameter.secure && !parameter.valueChanged
                    ? { name: parameter.name, originalName: parameter.originalName || parameter.name, secure: 1, preserve: true }
                    : { name: parameter.name, originalName: parameter.originalName || parameter.name, value: parameter.value, secure: flag(parameter.secure) })
            });

            const patches = taskSteps.map(step => {
                if (step.kind === 'Source' && step.source) {
                    const source = step.source;
                    const writeProfile = hostWriteProfile('Source', source.hostTypeCode);
                    if (!isKnownWriteProfile('Source', writeProfile))
                        return { kind: 'Source', id: source.id || step.id || '', index: step.index, patch: null };

                    if (writeProfile === HOST_WRITE_PROFILE_POP3) {
                        const patch = withId({
                            HostID: source.hostId,
                            Type: source.hostTypeCode,
                            FileMask: source.fileMask,
                            ExFile: source.ignoreFiles ? source.excludeFiles : '',
                            NewFilesOnly: flag(source.collectOnlyNewFiles),
                            Unzip: flag(source.expandZipFiles),
                            RetryIfNoFiles: flag(source.retryIfNoFilesFound),
                            DeleteOrig: Number(source.deleteOriginalAction) === 1 ? 1 : 0,
                            UseDefUser: flag(!source.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!source.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!source.overrideDataTimeout),
                            UseDefRetryCount: flag(!source.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!source.overrideRetryTimeout)
                        }, source.id || step.id);
                        applyMailCredentialPatch(patch, source);
                        optionalNumber(patch, 'ConnTimeoutSecs', source.connectTimeoutSeconds, source.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', source.dataTimeoutSeconds, source.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', source.retryCount, source.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', source.retryTimeoutSeconds, source.overrideRetryTimeout);
                        return { kind: 'Source', id: source.id || step.id || '', index: step.index, patch };
                    }

                    const patch = withId({
                        HostID: source.hostId,
                        Type: source.hostTypeCode,
                        FileMask: source.fileMask,
                        ExFo: source.ignoreFolders ? source.excludeFolders : '',
                        SearchSubdirs: flag(source.searchSubdirectories),
                        ExFile: source.ignoreFiles ? source.excludeFiles : '',
                        NewFilesOnly: flag(source.collectOnlyNewFiles),
                        Unzip: flag(source.expandZipFiles),
                        RetryIfNoFiles: flag(source.retryIfNoFilesFound),
                        DeleteOrig: Number(source.deleteOriginalAction || 0),
                        RenameTo: Number(source.deleteOriginalAction) === 2 ? source.renameTo : '',
                        DelRename: flag(source.renamedFilesMayOverwrite)
                    }, source.id || step.id);

                    if (writeProfile === HOST_WRITE_PROFILE_MOVEIT_TRANSFER) {
                        // MOVEit Transfer's editable Task folder is FolderName. Path is a different
                        // server-returned value and is deliberately left untouched for existing Steps.
                        patch.FolderName = source.path;
                        patch.FolderID = '';
                    } else {
                        patch.Path = source.path;
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_SHARE || writeProfile === HOST_WRITE_PROFILE_LOCAL) {
                        Object.assign(patch, {
                            UseDefRetryCount: flag(!source.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!source.overrideRetryTimeout),
                            UseDefRescanSecs: flag(!source.overrideRescanTime),
                            UDMxFi: flag(!source.overrideMaxFiles),
                            UDMxBy: flag(!source.overrideMaxBytes)
                        });
                        optionalNumber(patch, 'RetryCount', source.retryCount, source.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', source.retryTimeoutSeconds, source.overrideRetryTimeout);
                        optionalNumber(patch, 'RescanSecs', source.rescanSeconds, source.overrideRescanTime);
                        optionalNumber(patch, 'MxFi', source.maxFiles, source.overrideMaxFiles);
                        optionalNumber(patch, 'MxBy', source.maxBytes, source.overrideMaxBytes);
                        if (writeProfile === HOST_WRITE_PROFILE_SHARE) {
                            patch.UseDefWinCopyFileAPI = flag(!source.overrideWindowsCopyFileApi);
                            if (source.overrideWindowsCopyFileApi && source.useWindowsCopyFileApi != null) patch.UseWinCopyFileAPI = flag(source.useWindowsCopyFileApi);
                        }
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_MOVEIT_TRANSFER) {
                        Object.assign(patch, {
                            UseDefUser: flag(!source.overrideCredentials),
                            UseDefClientCert: flag(!source.overrideClientCertificate),
                            UseDefSort: flag(!source.overrideFileSorting),
                            UseDefConnTimeoutSecs: flag(!source.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!source.overrideDataTimeout),
                            UseDefRetryCount: flag(!source.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!source.overrideRetryTimeout),
                            UDMxFi: flag(!source.overrideMaxFiles),
                            UDMxBy: flag(!source.overrideMaxBytes)
                        });
                        if (source.overrideCredentials) {
                            patch.Username = String(source.username || '');
                            if (String(source.password || '').length) patch.Password = String(source.password);
                        }
                        if (source.overrideClientCertificate) {
                            patch.ClientCertSHA1 = String(source.clientCertificateSha1 || '');
                            patch.ClientCertStore = String(source.clientCertificateStore || '');
                            patch.ClientCertSubject = String(source.clientCertificateSubject || '');
                        }
                        if (source.overrideFileSorting) patch.Sort = String(source.sort || 'Filename,A');
                        optionalNumber(patch, 'ConnTimeoutSecs', source.connectTimeoutSeconds, source.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', source.dataTimeoutSeconds, source.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', source.retryCount, source.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', source.retryTimeoutSeconds, source.overrideRetryTimeout);
                        optionalNumber(patch, 'MxFi', source.maxFiles, source.overrideMaxFiles);
                        optionalNumber(patch, 'MxBy', source.maxBytes, source.overrideMaxBytes);
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_FTP) {
                        Object.assign(patch, {
                            UseDefUser: flag(!source.overrideCredentials),
                            UseDefClientCert: flag(!source.overrideClientCertificate),
                            UseDefConnTimeoutSecs: flag(!source.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!source.overrideDataTimeout),
                            UseDefRetryCount: flag(!source.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!source.overrideRetryTimeout),
                            UseDefRescanSecs: flag(!source.overrideRescanTime),
                            UseDefAB: flag(!source.overrideTransferType),
                            UseDefPassive: flag(!source.overridePassive),
                            UseDefXS: flag(!source.overrideXSha1),
                            UseDefBlindDL: flag(!source.overrideBlindDownload),
                            UseDefRsm: flag(!source.overrideResume),
                            UseDefReuseSSL: flag(!source.overrideReuseTlsSession),
                            UDMxFi: flag(!source.overrideMaxFiles),
                            UDMxBy: flag(!source.overrideMaxBytes),
                            UseDefMD5Get: flag(!source.overrideMd5Mode),
                            UseDefMD5File: flag(!source.overrideMd5FileName),
                            AddCom: String(source.additionalCommandsBefore || ''),
                            AddComAX: String(source.additionalCommandsAfter || '')
                        });
                        if (source.overrideCredentials) {
                            patch.Username = String(source.username || '');
                            if (String(source.password || '').length) patch.Password = String(source.password);
                            if (String(source.account || '').length) patch.Account = String(source.account);
                        }
                        if (source.overrideClientCertificate) {
                            patch.ClientCertSHA1 = String(source.clientCertificateSha1 || '');
                            patch.ClientCertStore = String(source.clientCertificateStore || '');
                            patch.ClientCertSubject = String(source.clientCertificateSubject || '');
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', source.connectTimeoutSeconds, source.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', source.dataTimeoutSeconds, source.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', source.retryCount, source.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', source.retryTimeoutSeconds, source.overrideRetryTimeout);
                        optionalNumber(patch, 'RescanSecs', source.rescanSeconds, source.overrideRescanTime);
                        optionalNumber(patch, 'MxFi', source.maxFiles, source.overrideMaxFiles);
                        optionalNumber(patch, 'MxBy', source.maxBytes, source.overrideMaxBytes);
                        if (source.overrideTransferType) patch.AB = String(source.transferType || 'binary').toLowerCase();
                        if (source.overridePassive && source.passive != null) patch.Passive = flag(source.passive);
                        if (source.overrideXSha1 && source.useXSha1 != null) patch.XS = flag(source.useXSha1);
                        if (source.overrideBlindDownload && source.blindDownload != null) patch.BlindDL = flag(source.blindDownload);
                        if (source.overrideResume && source.resume != null) patch.Rsm = flag(source.resume);
                        if (source.overrideReuseTlsSession && source.reuseTlsSession != null) patch.ReuseSSL = flag(source.reuseTlsSession);
                        if (source.overrideMd5Mode) patch.MD5Get = String(source.md5Mode || 'Never');
                        if (source.overrideMd5FileName) patch.MD5File = String(source.md5FileName || 'MD5SUM');
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_SFTP) {
                        Object.assign(patch, {
                            UseDefUser: flag(!source.overrideCredentials),
                            UseDefClientKey: flag(!source.overrideClientKey),
                            UseDefConnTimeoutSecs: flag(!source.overrideConnectTimeout),
                            UseDefRetryCount: flag(!source.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!source.overrideRetryTimeout),
                            UseDefRescanSecs: flag(!source.overrideRescanTime),
                            UseDefBlindDL: flag(!source.overrideBlindDownload),
                            UseDefRsm: flag(!source.overrideResume),
                            UDMxFi: flag(!source.overrideMaxFiles),
                            UDMxBy: flag(!source.overrideMaxBytes),
                            UseDefMD5Get: flag(!source.overrideMd5Mode),
                            UseDefMD5File: flag(!source.overrideMd5FileName)
                        });
                        if (source.overrideCredentials) {
                            patch.Username = String(source.username || '');
                            if (String(source.password || '').length) patch.Password = String(source.password);
                        }
                        if (source.overrideClientKey) patch.ClientKeyID = Number(source.clientKeyId || 0);
                        optionalNumber(patch, 'ConnTimeoutSecs', source.connectTimeoutSeconds, source.overrideConnectTimeout);
                        optionalNumber(patch, 'RetryCount', source.retryCount, source.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', source.retryTimeoutSeconds, source.overrideRetryTimeout);
                        optionalNumber(patch, 'RescanSecs', source.rescanSeconds, source.overrideRescanTime);
                        optionalNumber(patch, 'MxFi', source.maxFiles, source.overrideMaxFiles);
                        optionalNumber(patch, 'MxBy', source.maxBytes, source.overrideMaxBytes);
                        if (source.overrideBlindDownload && source.blindDownload != null) patch.BlindDL = flag(source.blindDownload);
                        if (source.overrideResume && source.resume != null) patch.Rsm = flag(source.resume);
                        if (source.overrideMd5Mode) patch.MD5Get = String(source.md5Mode || 'Never');
                        if (source.overrideMd5FileName) patch.MD5File = String(source.md5FileName || 'MD5SUM');
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_S3) {
                        Object.assign(patch, {
                            UseDefBucket: flag(source.useDefaultBucket),
                            UseDefUser: flag(!source.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!source.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!source.overrideDataTimeout),
                            UseDefRetryCount: flag(!source.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!source.overrideRetryTimeout),
                            UseDefRescanSecs: flag(!source.overrideRescanTime),
                            UDMxFi: flag(!source.overrideMaxFiles),
                            UDMxBy: flag(!source.overrideMaxBytes),
                            UseDefDLInteg: flag(!source.overrideDataIntegrity)
                        });
                        if (!source.useDefaultBucket) patch.Bucket = String(source.bucket || '');
                        if (source.overrideCredentials) {
                            patch.Username = String(source.username || '');
                            if (String(source.password || '').length) patch.Password = String(source.password);
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', source.connectTimeoutSeconds, source.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', source.dataTimeoutSeconds, source.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', source.retryCount, source.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', source.retryTimeoutSeconds, source.overrideRetryTimeout);
                        optionalNumber(patch, 'RescanSecs', source.rescanSeconds, source.overrideRescanTime);
                        optionalNumber(patch, 'MxFi', source.maxFiles, source.overrideMaxFiles);
                        optionalNumber(patch, 'MxBy', source.maxBytes, source.overrideMaxBytes);
                        if (source.overrideDataIntegrity) patch.DLInteg = String(source.dataIntegrityRule || 'never').toLowerCase();
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_AZURE_BLOB) {
                        Object.assign(patch, {
                            UseDefContainer: flag(source.useDefaultContainer),
                            UseDefUser: flag(!source.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!source.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!source.overrideDataTimeout),
                            UseDefRetryCount: flag(!source.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!source.overrideRetryTimeout),
                            UseDefRescanSecs: flag(!source.overrideRescanTime),
                            UDMxFi: flag(!source.overrideMaxFiles),
                            UDMxBy: flag(!source.overrideMaxBytes),
                            UseDefDLInteg: flag(!source.overrideDataIntegrity)
                        });
                        if (!source.useDefaultContainer) patch.Container = String(source.container || '');
                        if (source.overrideCredentials) {
                            patch.Username = String(source.username || '');
                            if (String(source.password || '').length) patch.Password = String(source.password);
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', source.connectTimeoutSeconds, source.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', source.dataTimeoutSeconds, source.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', source.retryCount, source.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', source.retryTimeoutSeconds, source.overrideRetryTimeout);
                        optionalNumber(patch, 'RescanSecs', source.rescanSeconds, source.overrideRescanTime);
                        optionalNumber(patch, 'MxFi', source.maxFiles, source.overrideMaxFiles);
                        optionalNumber(patch, 'MxBy', source.maxBytes, source.overrideMaxBytes);
                        if (source.overrideDataIntegrity) patch.DLInteg = String(source.dataIntegrityRule || 'never').toLowerCase();
                    }


                    if (writeProfile === HOST_WRITE_PROFILE_GOOGLE_STORAGE) {
                        Object.assign(patch, {
                            UseDefBucket: flag(source.useDefaultBucket),
                            UseDefUser: flag(!source.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!source.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!source.overrideDataTimeout),
                            UseDefRetryCount: flag(!source.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!source.overrideRetryTimeout),
                            UseDefRescanSecs: flag(!source.overrideRescanTime),
                            UDMxFi: flag(!source.overrideMaxFiles),
                            UDMxBy: flag(!source.overrideMaxBytes)
                        });
                        if (!source.useDefaultBucket) patch.Bucket = String(source.bucket || '');
                        if (source.overrideCredentials) {
                            const useOAuth = Boolean(source.googleUseOAuth);
                            patch.UseOAuth = useOAuth ? 1 : 0;
                            if (useOAuth) {
                                patch.ProjectID = String(source.googleProjectId || '');
                                patch.OAuthClientID = String(source.googleOAuthClientId || '');
                                if (String(source.googleOAuthClientSecret || '').length) patch.OAuthClientSecret = String(source.googleOAuthClientSecret);
                            } else {
                                patch.Username = String(source.username || '');
                                if (String(source.password || '').length) patch.Password = String(source.password);
                            }
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', source.connectTimeoutSeconds, source.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', source.dataTimeoutSeconds, source.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', source.retryCount, source.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', source.retryTimeoutSeconds, source.overrideRetryTimeout);
                        optionalNumber(patch, 'RescanSecs', source.rescanSeconds, source.overrideRescanTime);
                        optionalNumber(patch, 'MxFi', source.maxFiles, source.overrideMaxFiles);
                        optionalNumber(patch, 'MxBy', source.maxBytes, source.overrideMaxBytes);
                    }


                    if (writeProfile === HOST_WRITE_PROFILE_SHAREPOINT) {
                        const authenticationMode = Number(source.overrideCredentials
                            ? (source.sharePointAuthenticationMode ?? 2)
                            : (source.hostDefaults.sharePointAuthenticationMode ?? 2));
                        Object.assign(patch, {
                            UseDefDocumentLibrary: flag(source.useDefaultDocumentLibrary),
                            UseDefUser: flag(!source.overrideCredentials),
                            UseDefClientCert: flag(!source.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!source.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!source.overrideDataTimeout),
                            UseDefRetryCount: flag(!source.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!source.overrideRetryTimeout),
                            UseDefRescanSecs: flag(!source.overrideRescanTime),
                            UDMxFi: flag(!source.overrideMaxFiles),
                            UDMxBy: flag(!source.overrideMaxBytes)
                        });
                        if (source.overrideCredentials) {
                            patch.UsingAppAuthentication = authenticationMode;
                            if (authenticationMode === 1) {
                                patch.ClientID = String(source.sharePointClientId || '');
                                patch.RealmID = String(source.sharePointRealmId || '');
                                if (String(source.sharePointClientSecret || '').length) patch.ClientSecret = String(source.sharePointClientSecret);
                            } else if (authenticationMode === 2) {
                                const certificateSha1 = String(source.sharePointCertificateSha1 || source.clientCertificateSha1 || '');
                                const certificate = certificateBySha1(certificateSha1);
                                patch.ClientID = String(source.sharePointClientId || '');
                                patch.TenantID = String(source.sharePointTenantId || '');
                                patch.ClientCertSHA1 = certificateSha1;
                                patch.ClientCertStore = String(certificate?.store || source.clientCertificateStore || '');
                                patch.ClientCertSubject = String(certificate?.name || source.clientCertificateSubject || '');
                            }
                        }
                        if (!source.useDefaultDocumentLibrary) {
                            patch.DocumentLibrary = String(source.documentLibrary || '');
                            patch.DocumentLibraryGuid = String(source.documentLibraryGuid || '');
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', source.connectTimeoutSeconds, source.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', source.dataTimeoutSeconds, source.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', source.retryCount, source.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', source.retryTimeoutSeconds, source.overrideRetryTimeout);
                        optionalNumber(patch, 'RescanSecs', source.rescanSeconds, source.overrideRescanTime);
                        optionalNumber(patch, 'MxFi', source.maxFiles, source.overrideMaxFiles);
                        optionalNumber(patch, 'MxBy', source.maxBytes, source.overrideMaxBytes);
                    }

                    const criteria = buildSourceSpecialFilterCriteria(source);
                    if (criteria !== undefined) patch.Criteria = criteria;
                    return { kind: 'Source', id: source.id || step.id || '', index: step.index, patch };
                }

                if (step.kind === 'Process' && step.process) {
                    const process = step.process;
                    const patch = withId({
                        ScriptID: process.scriptId,
                        Run: process.run || 'PerFile',
                        IsDest: flag(process.isDestination)
                    }, process.id || step.id);
                    patch.Parameters = parameterPatch(process.parameters);
                    return { kind: 'Process', id: process.id || step.id || '', index: step.index, patch };
                }

                if (step.kind === 'Destination' && step.destination) {
                    const destination = step.destination;
                    const writeProfile = hostWriteProfile('Destination', destination.hostTypeCode);
                    if (!isKnownWriteProfile('Destination', writeProfile))
                        return { kind: 'Destination', id: destination.id || step.id || '', index: step.index, patch: null };

                    if (writeProfile === HOST_WRITE_PROFILE_SMTP) {
                        const patch = withId({
                            HostID: destination.hostId,
                            Type: destination.hostTypeCode,
                            AddressTo: String(destination.addressTo || ''),
                            Subject: String(destination.subject || ''),
                            Message: String(destination.message || ''),
                            IsHTML: flag(destination.isHtml),
                            Zip: flag(destination.zip),
                            UseOrigName: flag(destination.useOriginalName),
                            UseDefUser: flag(!destination.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!destination.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!destination.overrideDataTimeout),
                            UseDefRetryCount: flag(!destination.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!destination.overrideRetryTimeout)
                        }, destination.id || step.id);
                        if (!destination.useOriginalName) patch.FileName = String(destination.fileName || '');
                        applyMailCredentialPatch(patch, destination);
                        optionalNumber(patch, 'ConnTimeoutSecs', destination.connectTimeoutSeconds, destination.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', destination.dataTimeoutSeconds, destination.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', destination.retryCount, destination.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', destination.retryTimeoutSeconds, destination.overrideRetryTimeout);
                        return { kind: 'Destination', id: destination.id || step.id || '', index: step.index, patch };
                    }

                    const patch = withId({
                        HostID: destination.hostId,
                        Type: destination.hostTypeCode,
                        UseRelativeSubdirs: flag(destination.useRelativeSubdirectories),
                        ...((writeProfile === HOST_WRITE_PROFILE_S3 || writeProfile === HOST_WRITE_PROFILE_AZURE_BLOB || writeProfile === HOST_WRITE_PROFILE_GOOGLE_STORAGE) ? {} : { ForceDir: flag(destination.forceDirectory) }),
                        Zip: flag(destination.zip),
                        UseOrigName: flag(destination.useOriginalName),
                        FileName: destination.useOriginalName ? '[OrigName]' : destination.fileName,
                        OverwriteOrig: Number(destination.overwriteAction || 0)
                    }, destination.id || step.id);

                    if (writeProfile === HOST_WRITE_PROFILE_MOVEIT_TRANSFER) {
                        patch.FolderName = destination.path;
                        patch.FolderID = '';
                    } else {
                        patch.Path = destination.path;
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_SHARE || writeProfile === HOST_WRITE_PROFILE_LOCAL) {
                        Object.assign(patch, {
                            UseDefRetryCount: flag(!destination.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!destination.overrideRetryTimeout),
                            UseDefPreserveFileTimestamp: flag(!destination.overridePreserveFileTimestamp)
                        });
                        optionalNumber(patch, 'RetryCount', destination.retryCount, destination.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', destination.retryTimeoutSeconds, destination.overrideRetryTimeout);
                        if (writeProfile === HOST_WRITE_PROFILE_SHARE) {
                            patch.UseDefWinCopyFileAPI = flag(!destination.overrideWindowsCopyFileApi);
                            if (destination.overrideWindowsCopyFileApi && destination.useWindowsCopyFileApi != null) patch.UseWinCopyFileAPI = flag(destination.useWindowsCopyFileApi);
                        }
                        if (destination.overridePreserveFileTimestamp && destination.preserveFileTimestamp != null) patch.PreserveFileTimestamp = flag(destination.preserveFileTimestamp);
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_MOVEIT_TRANSFER) {
                        Object.assign(patch, {
                            UseDefUser: flag(!destination.overrideCredentials),
                            UseDefClientCert: flag(!destination.overrideClientCertificate),
                            UseDefConnTimeoutSecs: flag(!destination.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!destination.overrideDataTimeout),
                            UseDefRetryCount: flag(!destination.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!destination.overrideRetryTimeout)
                        });
                        if (destination.overrideCredentials) {
                            patch.Username = String(destination.username || '');
                            if (String(destination.password || '').length) patch.Password = String(destination.password);
                        }
                        if (destination.overrideClientCertificate) {
                            patch.ClientCertSHA1 = String(destination.clientCertificateSha1 || '');
                            patch.ClientCertStore = String(destination.clientCertificateStore || '');
                            patch.ClientCertSubject = String(destination.clientCertificateSubject || '');
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', destination.connectTimeoutSeconds, destination.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', destination.dataTimeoutSeconds, destination.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', destination.retryCount, destination.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', destination.retryTimeoutSeconds, destination.overrideRetryTimeout);
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_FTP) {
                        Object.assign(patch, {
                            UseDefUser: flag(!destination.overrideCredentials),
                            UseDefClientCert: flag(!destination.overrideClientCertificate),
                            UseDefConnTimeoutSecs: flag(!destination.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!destination.overrideDataTimeout),
                            UseDefRetryCount: flag(!destination.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!destination.overrideRetryTimeout),
                            UseDefAB: flag(!destination.overrideTransferType),
                            UseDefPassive: flag(!destination.overridePassive),
                            UseDefXS: flag(!destination.overrideXSha1),
                            UseDefBlindUL: flag(!destination.overrideBlindUpload),
                            UseDefRsm: flag(!destination.overrideResume),
                            UseDefReuseSSL: flag(!destination.overrideReuseTlsSession),
                            AddCom: String(destination.additionalCommandsBefore || ''),
                            AddComAX: String(destination.additionalCommandsAfter || '')
                        });
                        if (destination.overrideCredentials) {
                            patch.Username = String(destination.username || '');
                            if (String(destination.password || '').length) patch.Password = String(destination.password);
                            if (String(destination.account || '').length) patch.Account = String(destination.account);
                        }
                        if (destination.overrideClientCertificate) {
                            patch.ClientCertSHA1 = String(destination.clientCertificateSha1 || '');
                            patch.ClientCertStore = String(destination.clientCertificateStore || '');
                            patch.ClientCertSubject = String(destination.clientCertificateSubject || '');
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', destination.connectTimeoutSeconds, destination.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', destination.dataTimeoutSeconds, destination.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', destination.retryCount, destination.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', destination.retryTimeoutSeconds, destination.overrideRetryTimeout);
                        if (destination.overrideTransferType) patch.AB = String(destination.transferType || 'binary').toLowerCase();
                        if (destination.overridePassive && destination.passive != null) patch.Passive = flag(destination.passive);
                        if (destination.overrideXSha1 && destination.useXSha1 != null) patch.XS = flag(destination.useXSha1);
                        if (destination.overrideBlindUpload && destination.blindUpload != null) patch.BlindUL = flag(destination.blindUpload);
                        if (destination.overrideResume && destination.resume != null) patch.Rsm = flag(destination.resume);
                        if (destination.overrideReuseTlsSession && destination.reuseTlsSession != null) patch.ReuseSSL = flag(destination.reuseTlsSession);
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_SFTP) {
                        Object.assign(patch, {
                            UseDefUser: flag(!destination.overrideCredentials),
                            UseDefClientKey: flag(!destination.overrideClientKey),
                            UseDefConnTimeoutSecs: flag(!destination.overrideConnectTimeout),
                            UseDefRetryCount: flag(!destination.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!destination.overrideRetryTimeout),
                            UseDefRsm: flag(!destination.overrideResume),
                            UseDefSetFileAttr: flag(!destination.overrideFileAttributes),
                            UseDefPreserveFileTimestamp: flag(!destination.overridePreserveFileTimestamp)
                        });
                        if (destination.overrideCredentials) {
                            patch.Username = String(destination.username || '');
                            if (String(destination.password || '').length) patch.Password = String(destination.password);
                        }
                        if (destination.overrideClientKey) patch.ClientKeyID = Number(destination.clientKeyId || 0);
                        optionalNumber(patch, 'ConnTimeoutSecs', destination.connectTimeoutSeconds, destination.overrideConnectTimeout);
                        optionalNumber(patch, 'RetryCount', destination.retryCount, destination.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', destination.retryTimeoutSeconds, destination.overrideRetryTimeout);
                        if (destination.overrideResume && destination.resume != null) patch.Rsm = flag(destination.resume);
                        if (destination.overrideFileAttributes) {
                            patch.SetFileAttr = flag(destination.setFileAttributes);
                            if (destination.setFileAttributes) patch.DestFileAttr = String(destination.destinationFileAttributes || '');
                        }
                        if (destination.overridePreserveFileTimestamp && destination.preserveFileTimestamp != null)
                            patch.PreserveFileTimestamp = flag(destination.preserveFileTimestamp);
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_S3) {
                        Object.assign(patch, {
                            UseDefBucket: flag(destination.useDefaultBucket),
                            UseDefUser: flag(!destination.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!destination.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!destination.overrideDataTimeout),
                            UseDefRetryCount: flag(!destination.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!destination.overrideRetryTimeout)
                        });
                        if (!destination.useDefaultBucket) patch.Bucket = String(destination.bucket || '');
                        if (destination.overrideCredentials) {
                            patch.Username = String(destination.username || '');
                            if (String(destination.password || '').length) patch.Password = String(destination.password);
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', destination.connectTimeoutSeconds, destination.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', destination.dataTimeoutSeconds, destination.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', destination.retryCount, destination.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', destination.retryTimeoutSeconds, destination.overrideRetryTimeout);
                    }

                    if (writeProfile === HOST_WRITE_PROFILE_AZURE_BLOB) {
                        Object.assign(patch, {
                            UseDefContainer: flag(destination.useDefaultContainer),
                            UseDefUser: flag(!destination.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!destination.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!destination.overrideDataTimeout),
                            UseDefRetryCount: flag(!destination.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!destination.overrideRetryTimeout)
                        });
                        if (!destination.useDefaultContainer) patch.Container = String(destination.container || '');
                        if (destination.overrideCredentials) {
                            patch.Username = String(destination.username || '');
                            if (String(destination.password || '').length) patch.Password = String(destination.password);
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', destination.connectTimeoutSeconds, destination.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', destination.dataTimeoutSeconds, destination.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', destination.retryCount, destination.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', destination.retryTimeoutSeconds, destination.overrideRetryTimeout);
                    }


                    if (writeProfile === HOST_WRITE_PROFILE_GOOGLE_STORAGE) {
                        Object.assign(patch, {
                            UseDefBucket: flag(destination.useDefaultBucket),
                            UseDefUser: flag(!destination.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!destination.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!destination.overrideDataTimeout),
                            UseDefRetryCount: flag(!destination.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!destination.overrideRetryTimeout)
                        });
                        if (!destination.useDefaultBucket) patch.Bucket = String(destination.bucket || '');
                        if (destination.overrideCredentials) {
                            const useOAuth = Boolean(destination.googleUseOAuth);
                            patch.UseOAuth = useOAuth ? 1 : 0;
                            if (useOAuth) {
                                patch.ProjectID = String(destination.googleProjectId || '');
                                patch.OAuthClientID = String(destination.googleOAuthClientId || '');
                                if (String(destination.googleOAuthClientSecret || '').length) patch.OAuthClientSecret = String(destination.googleOAuthClientSecret);
                            } else {
                                patch.Username = String(destination.username || '');
                                if (String(destination.password || '').length) patch.Password = String(destination.password);
                            }
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', destination.connectTimeoutSeconds, destination.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', destination.dataTimeoutSeconds, destination.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', destination.retryCount, destination.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', destination.retryTimeoutSeconds, destination.overrideRetryTimeout);
                    }


                    if (writeProfile === HOST_WRITE_PROFILE_SHAREPOINT) {
                        const authenticationMode = Number(destination.overrideCredentials
                            ? (destination.sharePointAuthenticationMode ?? 2)
                            : (destination.hostDefaults.sharePointAuthenticationMode ?? 2));
                        Object.assign(patch, {
                            UseDefDocumentLibrary: flag(destination.useDefaultDocumentLibrary),
                            UseDefUser: flag(!destination.overrideCredentials),
                            UseDefClientCert: flag(!destination.overrideCredentials),
                            UseDefConnTimeoutSecs: flag(!destination.overrideConnectTimeout),
                            UseDefDataTimeoutSecs: flag(!destination.overrideDataTimeout),
                            UseDefRetryCount: flag(!destination.overrideRetryCount),
                            UseDefRetryTimeoutSecs: flag(!destination.overrideRetryTimeout)
                        });
                        if (destination.overrideCredentials) {
                            patch.UsingAppAuthentication = authenticationMode;
                            if (authenticationMode === 1) {
                                patch.ClientID = String(destination.sharePointClientId || '');
                                patch.RealmID = String(destination.sharePointRealmId || '');
                                if (String(destination.sharePointClientSecret || '').length) patch.ClientSecret = String(destination.sharePointClientSecret);
                            } else if (authenticationMode === 2) {
                                const certificateSha1 = String(destination.sharePointCertificateSha1 || destination.clientCertificateSha1 || '');
                                const certificate = certificateBySha1(certificateSha1);
                                patch.ClientID = String(destination.sharePointClientId || '');
                                patch.TenantID = String(destination.sharePointTenantId || '');
                                patch.ClientCertSHA1 = certificateSha1;
                                patch.ClientCertStore = String(certificate?.store || destination.clientCertificateStore || '');
                                patch.ClientCertSubject = String(certificate?.name || destination.clientCertificateSubject || '');
                            }
                        }
                        if (!destination.useDefaultDocumentLibrary) {
                            patch.DocumentLibrary = String(destination.documentLibrary || '');
                            patch.DocumentLibraryGuid = String(destination.documentLibraryGuid || '');
                        }
                        optionalNumber(patch, 'ConnTimeoutSecs', destination.connectTimeoutSeconds, destination.overrideConnectTimeout);
                        optionalNumber(patch, 'DataTimeoutSecs', destination.dataTimeoutSeconds, destination.overrideDataTimeout);
                        optionalNumber(patch, 'RetryCount', destination.retryCount, destination.overrideRetryCount);
                        optionalNumber(patch, 'RetryTimeoutSecs', destination.retryTimeoutSeconds, destination.overrideRetryTimeout);
                    }

                    return { kind: 'Destination', id: destination.id || step.id || '', index: step.index, patch };
                }

                return { kind: step.kind || 'Step', id: step.id || '', index: step.index, patch: null };
            });
            if (isSyncTask()) patches.forEach(item => {
                if (!item.patch) return;
                syncTraditionalFields.forEach(field => { delete item.patch[field]; });
                const source = taskSteps[item.index]?.source;
                if (source) {
                    item.patch.UseDefPreserveFileTimestamp = source.overridePreserveFileTimestamp ? 0 : 1;
                    if (source.overridePreserveFileTimestamp) item.patch.PreserveFileTimestamp = source.preserveFileTimestamp ? 1 : 0;
                }
                if (item.patch.Type === 'FileSystem') {
                    const allowed = new Set(['ID','HostID','Type','Path','FileMask','ExFo','SearchSubdirs','ExFile','Criteria','UseDefPreserveFileTimestamp','PreserveFileTimestamp']);
                    Object.keys(item.patch).forEach(field => { if (!allowed.has(field)) delete item.patch[field]; });
                }
            });
            if (isAdvancedTask()) patches.forEach((item,index) => item.clientKey = taskSteps[index].key);
            return patches;
        }

        function buildScheduleSaveItems() {
            const physical = [];
            const append = item => {
                item.index = physical.length;
                physical.push(item);
            };

            schedules.forEach(schedule => {
                const common = {
                    daysOfWeek: [...(schedule.daysOfWeek || [])],
                    daysOfMonth: [...(schedule.daysOfMonth || [])],
                    dateLists: (schedule.dateLists || []).map(reference => ({ id: reference.id, run: reference.run })),
                    onlyUntilFirstSuccess: Boolean(schedule.onlyUntilFirstSuccess),
                    failIfNoSuccessInSchedule: Boolean(schedule.failIfNoSuccessInSchedule),
                    runEvenIfNotifications: Boolean(schedule.runEvenIfNotifications)
                };
                const logicalOvernight = scheduleHasCrossingInterval(schedule);
                const persistedAsNativePair = isPairedOvernightSchedule(schedule);

                // The editor models one logical Schedule.  MOVEit's physical representation is
                // chosen from the CURRENT values, not from how the Schedule happened to be stored
                // when the editor opened.  This makes same-day <-> overnight transitions transparent.
                if (!logicalOvernight) {
                    const recreateFromPair = persistedAsNativePair;
                    append({
                        ...common,
                        serverIndex: recreateFromPair ? -1 : schedule.serverIndex,
                        cloneFromServerIndex: recreateFromPair ? schedule.serverIndex : (schedule.cloneFromServerIndex ?? -1),
                        id: recreateFromPair ? '' : schedule.id,
                        multi: 0,
                        intervals: (schedule.intervals || []).map(interval => ({
                            serverIndex: interval.serverIndex,
                            id: recreateFromPair ? '' : interval.id,
                            startTime: interval.startTime,
                            // Native one-time schedules use StartTime == EndTime and EveryMinutes=0.
                            endTime: interval.repeat ? interval.endTime : interval.startTime,
                            everyMinutes: interval.repeat ? interval.everyMinutes : 0
                        }))
                    });
                    return;
                }

                // Captured native MOVEit 2025.1 overnight representation:
                //   Multi=1: selected start day(s), StartTime -> 23:59
                //   Multi=2: following weekday(s) and/or DayOfMonth value(s), next repeat-cadence
                //            occurrence after midnight -> EndTime
                // Existing native pairs retain their shared IDs.  A normal/new Schedule crossing
                // midnight is rebuilt as a new linked pair by the server/native XML boundary.
                const pairKey = `overnight:${schedule.key}`;
                const afterDaysOfWeek = shiftScheduleWeekdaysForward(schedule.daysOfWeek || []);
                const afterDaysOfMonth = shiftScheduleMonthDaysForward(schedule.daysOfMonth || []);
                const cloneFromServerIndex = schedule.cloneFromServerIndex ?? -1;
                const generatedPairTemplateIndex = schedule.serverIndex >= 0 ? schedule.serverIndex : cloneFromServerIndex;
                append({
                    ...common,
                    pairKey,
                    pairRole: 'beforeMidnight',
                    serverIndex: persistedAsNativePair ? schedule.serverIndex : (schedule.serverIndex >= 0 ? schedule.serverIndex : -1),
                    cloneFromServerIndex: persistedAsNativePair ? -1 : cloneFromServerIndex,
                    sourceId: persistedAsNativePair ? '' : String(schedule.id || ''),
                    id: persistedAsNativePair ? schedule.id : '',
                    multi: 1,
                    dateLists: [],
                    intervals: (schedule.intervals || []).map(interval => ({
                        serverIndex: interval.serverIndex,
                        id: persistedAsNativePair ? interval.id : '',
                        startTime: interval.startTime,
                        endTime: '23:59',
                        everyMinutes: interval.repeat ? interval.everyMinutes : 0
                    }))
                });
                append({
                    ...common,
                    daysOfWeek: afterDaysOfWeek,
                    daysOfMonth: afterDaysOfMonth,
                    pairKey,
                    pairRole: 'afterMidnight',
                    serverIndex: persistedAsNativePair ? (schedule.pairedServerIndex ?? -1) : -1,
                    cloneFromServerIndex: persistedAsNativePair ? -1 : generatedPairTemplateIndex,
                    sourceId: persistedAsNativePair ? '' : String(schedule.id || ''),
                    id: persistedAsNativePair ? (schedule.pairedId || '') : '',
                    multi: 2,
                    dateLists: [],
                    intervals: (schedule.intervals || []).map(interval => ({
                        serverIndex: persistedAsNativePair ? (interval.pairedServerIndex ?? interval.serverIndex) : (interval.serverIndex ?? -1),
                        id: persistedAsNativePair ? (interval.pairedId || '') : '',
                        startTime: overnightPostStartTime(interval.startTime, Number(interval.everyMinutes || 0)),
                        endTime: interval.endTime,
                        everyMinutes: interval.repeat ? interval.everyMinutes : 0
                    }))
                });
            });

            return physical;
        }

        function refreshSyncSettingsPresentation() {
            const direction = dialog.querySelector('[data-editor-field="syncDirection"]');
            const deletion = dialog.querySelector('[data-editor-field="syncDeletePolicy"]');
            if (deletion?.options[2]) deletion.options[2].disabled = direction?.value === 'bidirectional';
            const initial = dialog.querySelector('[data-editor-field="syncInitialRun"]');
            const tolerance = dialog.querySelector('[data-sync-tolerance-row]');
            if (tolerance) tolerance.hidden = initial?.value !== 'custom';
        }

        function buildSyncSettingsPatch() {
            if (!isSyncTask()) return null;
            const patch = {};
            const control = name => dialog.querySelector(`[data-editor-field="${name}"]`);
            const changed = name => control(name) && readControl(control(name)) !== baseline.get(name);
            if (changed('syncDirection')) patch.direction = control('syncDirection').value;
            if (changed('syncDeletePolicy')) patch.deletePolicy = Number(control('syncDeletePolicy').value);
            if (changed('syncCaseSensitive')) patch.caseSensitive = control('syncCaseSensitive').checked;
            if (changed('syncInitialRun') || changed('syncSameSeconds')) patch.sameSeconds = control('syncInitialRun').value === 'custom'
                ? Number(control('syncSameSeconds').value) : Number(control('syncInitialRun').value);
            return Object.keys(patch).length ? patch : null;
        }

        function buildSaveRequest() {
            const field = name => editorFields().find(control => control.dataset.editorField === name);
            const value = name => field(name) ? readControl(field(name)) : '';
            const numberOrNull = name => {
                const text = value(name).trim();
                if (!text) return null;
                const parsed = Number(text);
                return Number.isFinite(parsed) ? parsed : null;
            };
            const metadata = {};
            editorFields().forEach(control => {
                const key = control.dataset.editorField || '';
                if (key.startsWith('metadata:')) metadata[key.slice('metadata:'.length)] = readControl(control);
            });
            return {
                task: {
                    name: value('name'),
                    description: value('description'),
                    operatorNotes: value('operatorNotes'),
                    enabled: value('enabled') === 'true',
                    cacheNames: value('cacheNames'),
                    useGlobalTaskLogRetention: value('useGlobalTaskLogRetention') === 'true',
                    taskLogsToKeep: value('useGlobalTaskLogRetention') === 'true' ? null : numberOrNull('taskLogsToKeep'),
                    stateCacheTime: numberOrNull('stateCacheTime'),
                    stateCacheUnit: value('stateCacheUnit'),
                    useDefaultStateCaching: value('useDefaultStateCaching') === 'true',
                    autoRerun: value('autoRerun') === 'true'
                },
                advancedFlow: isAdvancedTask() ? advancedEditor?.getDraft() : null,
                syncSettings: buildSyncSettingsPatch(),
                steps: buildMoveItStepPatches(),
                schedules: buildScheduleSaveItems(),
                nativeScheduleXmlRequired: schedulesBaselineHadNativeMulti || schedules.some(schedule =>
                    isPairedOvernightSchedule(schedule) || Number(schedule?.multi || 0) !== 0 || scheduleHasCrossingInterval(schedule)),
                parameters: {
                    replace: true,
                    Parameter: taskParameters.map(parameter => parameter.secure && !parameter.valueChanged
                        ? { name: parameter.name, originalName: parameter.originalName || parameter.name, secure: true, preserve: true }
                        : { name: parameter.name, originalName: parameter.originalName || parameter.name, value: parameter.value, secure: parameter.secure })
                },
                nextActions: nextActions.map(action => ({
                    clientKey: action.key, wait: Boolean(action.wait), index: action.index, serverIndex: action.serverIndex, cloneFromServerIndex: action.cloneFromServerIndex ?? -1, id: action.id, type: action.type, doAfter: action.doAfter,
                    doIfFailure: action.doIfFailure, doIfNoAction: action.doIfNoAction, doIfSuccess: action.doIfSuccess,
                    isHtml: action.isHtml, hostId: action.hostId, addressTo: action.addressTo, subject: action.subject,
                    message: action.message, taskId: action.taskId,
                    parameters: {
                        replace: true,
                        any: action.parameters.map(parameter => parameter.secure && !parameter.valueChanged
                            ? { name: parameter.name, originalName: parameter.originalName || parameter.name, secure: true, preserve: true }
                            : { name: parameter.name, originalName: parameter.originalName || parameter.name, value: parameter.value, secure: parameter.secure })
                    }
                })),
                metadata,
                folderId: value('folderId') ? Number(value('folderId')) : null
            };
        }

        function validateScheduleStateForSave() {
            for (let scheduleIndex = 0; scheduleIndex < schedules.length; scheduleIndex++) {
                const schedule = schedules[scheduleIndex];
                const scheduleNumber = scheduleIndex + 1;
                const weekdays = (schedule.daysOfWeek || []).filter(value => String(value || '').trim());
                const monthDays = (schedule.daysOfMonth || []).filter(value => Number.isInteger(Number(value)) && Number(value) >= 1 && Number(value) <= 31);
                const dateLists = schedule.dateLists || [];
                const incompleteDateList = dateLists.some(reference => !String(reference?.id || '').trim());
                if (incompleteDateList) return `Schedule ${scheduleNumber}: Select a Date List for each Custom entry or remove the incomplete entry.`;
                const selectedDateLists = dateLists.filter(reference => String(reference?.id || '').trim());
                if (!weekdays.length && !monthDays.length && !selectedDateLists.length) {
                    const dayMode = ['week', 'month', 'custom'].includes(schedule.uiDayMode) ? schedule.uiDayMode : inferScheduleDayMode(schedule);
                    if (dayMode === 'month') return `Schedule ${scheduleNumber}: Select at least one day of the month.`;
                    if (dayMode === 'custom') return `Schedule ${scheduleNumber}: Select at least one Date List.`;
                    return `Schedule ${scheduleNumber}: Select at least one day of the week.`;
                }

                const intervals = schedule.intervals || [];
                for (const interval of intervals) {
                    if (scheduleTimeMinutes(interval.startTime) == null) return 'Enter a valid Schedule Start Time.';
                    if (scheduleTimeMinutes(interval.endTime) == null) return 'Enter a valid Schedule End Time.';
                    if (interval.repeat && Number(interval.everyMinutes || 0) <= 0) return 'Repeat interval must be greater than zero minutes.';
                }

                const crossing = scheduleHasCrossingInterval(schedule);
                if (crossing && (schedule.dateLists || []).some(reference => String(reference.id || '').trim())) {
                    return 'MOVEit Date List schedules must remain within the selected date and cannot span midnight. Use a separate Schedule for any overnight interval.';
                }

                if (crossing) {
                    // We have verified the native representation for one linked overnight interval.
                    // Do not invent the wire shape for a mixed/multiple-time overnight Schedule.
                    if (intervals.length !== 1) {
                        return 'This Schedule contains multiple time rows and at least one crosses midnight. MAUI needs the native MOVEit XML pattern for that exact case before it can save it safely.';
                    }
                    const interval = intervals[0];
                    const start = scheduleTimeMinutes(interval.startTime);
                    const end = scheduleTimeMinutes(interval.endTime);
                    const every = Number(interval.everyMinutes || 0);
                    if (start == null || end == null) return 'The overnight Schedule contains an invalid Start Time or End Time.';
                    if (!interval.repeat || every <= 0) return 'An overnight Schedule must use Repeat with a positive interval.';
                    if (start <= end) return 'An overnight Schedule must have an End Time earlier than its Start Time.';
                    if (overnightPostStartMinutes(start, every) == null) return 'The overnight Schedule Repeat interval is invalid.';
                }
            }
            return '';
        }

        function numericOverrideError(value, label) {
            if (value == null || value === '') return `${label} is required.`;
            const number = Number(value);
            if (!Number.isInteger(number) || number < 0) return `${label} must be a whole number of zero or greater.`;
            return '';
        }

        function validateSaveRequest(request) {
            if (request.syncSettings) {
                if (taskSteps.length !== 2) return 'Define Folder A and Folder B before changing Sync options.';
                const direction = dialog.querySelector('[data-editor-field="syncDirection"]')?.value;
                const deletes = Number(dialog.querySelector('[data-editor-field="syncDeletePolicy"]')?.value);
                if (direction === 'bidirectional' && deletes === 2) return 'Choose a bidirectional Sync Deletes policy.';
                const seconds = request.syncSettings.sameSeconds;
                if (seconds != null && (!Number.isInteger(seconds) || seconds < -3 || seconds > 2147483647)) return 'Initial Run timestamp tolerance must be a whole number of seconds.';
                if (dialog.querySelector('[data-editor-field="syncInitialRun"]')?.value === 'custom'
                    && !String(dialog.querySelector('[data-editor-field="syncSameSeconds"]')?.value || '').trim()) return 'Enter the Initial Run timestamp tolerance.';
            }
            if (isSyncTask() && request.task.enabled && taskSteps.length !== 2) return 'Define Folder A and Folder B before enabling the Sync Task.';
            if (!String(request.task.name || '').trim()) return 'Task Name is required.';
            const incomplete = incompleteHostStep();
            if (incomplete) return `Select a Host for the ${incomplete.kind} before saving.`;
            for (const step of taskSteps) {
                if (step.kind === 'Source' && step.source?.serverHostTypeCode
                    && !canChangeHost('Source', step.source.serverHostTypeCode, step.source.hostTypeCode))
                    return `Changing this Source from ${hostTypeLabel(step.source.serverHostTypeCode)} to ${step.source.hostType || hostTypeLabel(step.source.hostTypeCode)} is not part of a verified MOVEit Change Host profile.`;
                if (step.kind === 'Destination' && step.destination?.serverHostTypeCode
                    && !canChangeHost('Destination', step.destination.serverHostTypeCode, step.destination.hostTypeCode))
                    return `Changing this Destination from ${hostTypeLabel(step.destination.serverHostTypeCode)} to ${step.destination.hostType || hostTypeLabel(step.destination.hostTypeCode)} is not part of a verified MOVEit Change Host profile.`;
                if (step.kind === 'Source' && step.source && !canWriteHostStep('Source', step.source.hostTypeCode) && !isPreservedUnsupportedHostStep(step))
                    return `This ${step.source.hostType || hostTypeLabel(step.source.hostTypeCode)} Source does not have a verified MAUI write profile. Leave it unchanged, change it to a supported Host, or remove it before saving.`;
                if (step.kind === 'Destination' && step.destination && !canWriteHostStep('Destination', step.destination.hostTypeCode) && !isPreservedUnsupportedHostStep(step))
                    return `This ${step.destination.hostType || hostTypeLabel(step.destination.hostTypeCode)} Destination does not have a verified MAUI write profile. Leave it unchanged, change it to a supported Host, or remove it before saving.`;
                if (step.kind === 'Source' && step.source?.requiresFolderSelection && !String(step.source.path || '').trim())
                    return 'Select the Source Folder(s) for the newly selected Host before saving.';
                if (step.kind === 'Destination' && step.destination?.requiresFolderSelection && !String(step.destination.path || '').trim())
                    return 'Select the Destination Folder(s) for the newly selected Host before saving.';

                if (step.kind === 'Source' && step.source && canWriteHostStep('Source', step.source.hostTypeCode)) {
                    const source = step.source;
                    const sourceProfile = hostWriteProfile('Source', source.hostTypeCode);
                    if (!String(source.fileMask || '').trim()) return 'Source File(s) is required. Enter a filename or wildcard such as *.*.';
                    if (source.overrideRetryCount) { const error = numericOverrideError(source.retryCount, 'Source Retry Count'); if (error) return error; }
                    if (source.overrideRetryTimeout) { const error = numericOverrideError(source.retryTimeoutSeconds, 'Source Retry Timeout'); if (error) return error; }
                    if (source.overrideMaxFiles) { const error = numericOverrideError(source.maxFiles, 'Source File Count Limit'); if (error) return error; }
                    if (source.overrideMaxBytes) { const error = numericOverrideError(source.maxBytes, 'Source File Size Limit'); if (error) return error; }
                    if ((sourceProfile === HOST_WRITE_PROFILE_LOCAL || sourceProfile === HOST_WRITE_PROFILE_SHARE || sourceProfile === HOST_WRITE_PROFILE_FTP || sourceProfile === HOST_WRITE_PROFILE_SFTP || sourceProfile === HOST_WRITE_PROFILE_S3 || sourceProfile === HOST_WRITE_PROFILE_AZURE_BLOB || sourceProfile === HOST_WRITE_PROFILE_SHAREPOINT) && source.overrideRescanTime) {
                        const error = numericOverrideError(source.rescanSeconds, 'Source Transfer Rescan Time');
                        if (error) return error;
                    }
                    if (sourceProfile === HOST_WRITE_PROFILE_MOVEIT_TRANSFER || sourceProfile === HOST_WRITE_PROFILE_FTP || sourceProfile === HOST_WRITE_PROFILE_SFTP || sourceProfile === HOST_WRITE_PROFILE_S3 || sourceProfile === HOST_WRITE_PROFILE_AZURE_BLOB) {
                        const hostLabel = sourceProfile === HOST_WRITE_PROFILE_FTP ? 'FTP/FTPS' : sourceProfile === HOST_WRITE_PROFILE_SFTP ? 'SFTP' : sourceProfile === HOST_WRITE_PROFILE_S3 ? 'Amazon S3' : sourceProfile === HOST_WRITE_PROFILE_AZURE_BLOB ? 'Azure Blob' : 'MOVEit Transfer';
                        if (source.overrideCredentials) {
                            if (!String(source.username || '').trim()) return `${hostLabel} Source ${sourceProfile === HOST_WRITE_PROFILE_S3 ? 'Access Key ID' : sourceProfile === HOST_WRITE_PROFILE_AZURE_BLOB ? 'Storage Account' : 'Username'} is required when default credentials are overridden.`;
                            if (!source.passwordConfigured && !String(source.password || '').length) return `Set the ${hostLabel} Source ${sourceProfile === HOST_WRITE_PROFILE_S3 ? 'Secret Access Key' : sourceProfile === HOST_WRITE_PROFILE_AZURE_BLOB ? 'Key' : 'Password'} when default credentials are overridden.`;
                        }
                        if (sourceProfile !== HOST_WRITE_PROFILE_SFTP && sourceProfile !== HOST_WRITE_PROFILE_S3 && sourceProfile !== HOST_WRITE_PROFILE_AZURE_BLOB && source.overrideClientCertificate && String(source.clientCertificateSha1 || '').trim()
                            && (!String(source.clientCertificateStore || '').trim() || !String(source.clientCertificateSubject || '').trim()))
                            return `Select a complete ${hostLabel} Source client certificate.`;
                        if (sourceProfile === HOST_WRITE_PROFILE_SFTP && source.overrideClientKey && String(source.clientKeyId || '').trim()) {
                            const keyId = Number(source.clientKeyId);
                            if (!Number.isInteger(keyId) || keyId < 0) return 'SFTP Source Client Key is invalid.';
                        }
                        if (source.overrideConnectTimeout) { const error = numericOverrideError(source.connectTimeoutSeconds, `${hostLabel} Source Connect Timeout`); if (error) return error; }
                        if (sourceProfile !== HOST_WRITE_PROFILE_SFTP && source.overrideDataTimeout) { const error = numericOverrideError(source.dataTimeoutSeconds, `${hostLabel} Source Data Timeout`); if (error) return error; }
                    }
                    if (sourceProfile === HOST_WRITE_PROFILE_FTP) {
                        if (source.overrideTransferType && !['ascii', 'binary'].includes(String(source.transferType || '').toLowerCase()))
                            return 'FTP/FTPS Source Transfer Type must be ASCII or Binary.';
                        if (source.overrideMd5Mode && !['Never', 'IfPresent', 'Always'].includes(String(source.md5Mode || '')))
                            return 'FTP/FTPS Source Use MD5 must be Never, If Present, or Always (required).';
                        if (source.overrideMd5FileName && !String(source.md5FileName || '').trim())
                            return 'FTP/FTPS Source MD5 File Name is required when its default is overridden.';
                    }
                    if (sourceProfile === HOST_WRITE_PROFILE_SFTP) {
                        if (source.overrideMd5Mode && !['Never', 'IfPresent', 'Always'].includes(String(source.md5Mode || '')))
                            return 'SFTP Source Use MD5 must be Never, If Present, or Always (required).';
                        if (source.overrideMd5FileName && !String(source.md5FileName || '').trim())
                            return 'SFTP Source MD5 File Name is required when its default is overridden.';
                    }
                    if (sourceProfile === HOST_WRITE_PROFILE_S3) {
                        if (!source.useDefaultBucket && !String(source.bucket || '').trim())
                            return 'Amazon S3 Source Bucket Name is required when the Host default bucket is overridden.';
                        if (source.overrideDataIntegrity && !['never', 'ifsure', 'ignore', 'always'].includes(String(source.dataIntegrityRule || '').toLowerCase()))
                            return 'Select a valid Amazon S3 Source data integrity checking rule.';
                    }
                    if (sourceProfile === HOST_WRITE_PROFILE_AZURE_BLOB) {
                        if (!source.useDefaultContainer && !String(source.container || '').trim())
                            return 'Azure Blob Source Container is required when the Host default container is overridden.';
                        if (source.overrideDataIntegrity && !['never', 'ignore', 'always'].includes(String(source.dataIntegrityRule || '').toLowerCase()))
                            return 'Select a valid Azure Blob Source data integrity checking rule.';
                    }
                    if (sourceProfile === HOST_WRITE_PROFILE_GOOGLE_STORAGE) {
                        if (source.overrideRescanTime) { const error = numericOverrideError(source.rescanSeconds, 'Google Cloud Storage Source Transfer Rescan Time'); if (error) return error; }
                        if (!source.useDefaultBucket && !String(source.bucket || '').trim())
                            return 'Google Cloud Storage Source Bucket Name is required when the Host default bucket is overridden.';
                        if (source.overrideCredentials) {
                            if (Boolean(source.googleUseOAuth)) {
                                if (!String(source.googleOAuthClientId || '').trim()) return 'Google Cloud Storage Source OAuth Client ID is required.';
                                if (!String(source.googleProjectId || '').trim()) return 'Google Cloud Storage Source OAuth Project ID is required.';
                                if (!source.googleOAuthClientSecretConfigured && !String(source.googleOAuthClientSecret || '').length) return 'Set the Google Cloud Storage Source OAuth private key.';
                            } else {
                                if (!String(source.username || '').trim()) return 'Google Cloud Storage Source Access Key ID is required.';
                                if (!source.passwordConfigured && !String(source.password || '').length) return 'Set the Google Cloud Storage Source Secret Access Key.';
                            }
                        }
                        if (source.overrideConnectTimeout) { const error = numericOverrideError(source.connectTimeoutSeconds, 'Google Cloud Storage Source Connect Timeout'); if (error) return error; }
                        if (source.overrideDataTimeout) { const error = numericOverrideError(source.dataTimeoutSeconds, 'Google Cloud Storage Source Data Timeout'); if (error) return error; }
                    }
                    if (sourceProfile === HOST_WRITE_PROFILE_POP3) {
                        if (source.overrideCredentials) {
                            if (Boolean(source.emailUseOAuth)) {
                                if (!String(source.emailOAuthUsername || '').trim()) return 'POP3 Source OAuth Username is required.';
                                if (!String(source.emailOAuthClientId || '').trim()) return 'POP3 Source OAuth Client ID is required.';
                                if (!source.emailOAuthClientSecretConfigured && !String(source.emailOAuthClientSecret || '').length) return 'Set the POP3 Source OAuth Client Secret.';
                                if (String(source.emailOAuthType || 'microsoft365').toLowerCase() === 'microsoft365') {
                                    if (!String(source.emailOAuthTenantId || '').trim()) return 'POP3 Source OAuth Tenant ID is required for Microsoft Office 365.';
                                } else {
                                    if (!String(source.emailOAuthScope || '').trim()) return 'POP3 Source OAuth Authorization Scope is required for Custom authentication.';
                                    if (!String(source.emailOAuthTokenUrl || '').trim()) return 'POP3 Source OAuth Token URL is required for Custom authentication.';
                                }
                            } else {
                                if (!String(source.username || '').trim()) return 'POP3 Source Username is required when default credentials are overridden.';
                                if (!source.passwordConfigured && !String(source.password || '').length) return 'Set the POP3 Source Password when default credentials are overridden.';
                            }
                        }
                        if (source.overrideConnectTimeout) { const error = numericOverrideError(source.connectTimeoutSeconds, 'POP3 Source Connect Timeout'); if (error) return error; }
                        if (source.overrideDataTimeout) { const error = numericOverrideError(source.dataTimeoutSeconds, 'POP3 Source Data Timeout'); if (error) return error; }
                    }
                    if (sourceProfile === HOST_WRITE_PROFILE_SHAREPOINT) {
                        if (!source.useDefaultDocumentLibrary) {
                            if (!String(source.documentLibrary || '').trim()) return 'SharePoint Source Document Library is required when the Host default library is overridden.';
                            if (!String(source.documentLibraryGuid || '').trim()) return 'Select the SharePoint Source Document Library using Browse so its library ID is known.';
                        }
                        if (source.overrideCredentials) {
                            const mode = Number(source.sharePointAuthenticationMode ?? 2);
                            if (mode === 1) {
                                if (!String(source.sharePointClientId || '').trim()) return 'SharePoint Source Client ID is required for App Access.';
                                if (!String(source.sharePointRealmId || '').trim()) return 'SharePoint Source Realm ID is required for App Access.';
                                if (!source.sharePointClientSecretConfigured && !String(source.sharePointClientSecret || '').length) return 'Set the SharePoint Source Client Secret for App Access.';
                            } else if (mode === 2) {
                                if (!String(source.sharePointClientId || '').trim()) return 'SharePoint Source Client ID is required for Certificate Access.';
                                if (!String(source.sharePointTenantId || '').trim()) return 'SharePoint Source Tenant ID is required for Certificate Access.';
                                if (!String(source.sharePointCertificateSha1 || '').trim()) return 'Select a SharePoint Source Certificate for Certificate Access.';
                            } else return 'Select a valid SharePoint Source authentication method.';
                        }
                        if (source.overrideConnectTimeout) { const error = numericOverrideError(source.connectTimeoutSeconds, 'SharePoint Source Connect Timeout'); if (error) return error; }
                        if (source.overrideDataTimeout) { const error = numericOverrideError(source.dataTimeoutSeconds, 'SharePoint Source Data Timeout'); if (error) return error; }
                    }
                }

                if (step.kind === 'Destination' && step.destination && canWriteHostStep('Destination', step.destination.hostTypeCode)) {
                    const destination = step.destination;
                    const destinationProfile = hostWriteProfile('Destination', destination.hostTypeCode);
                    if (destination.overrideRetryCount) { const error = numericOverrideError(destination.retryCount, 'Destination Retry Count'); if (error) return error; }
                    if (destination.overrideRetryTimeout) { const error = numericOverrideError(destination.retryTimeoutSeconds, 'Destination Retry Timeout'); if (error) return error; }
                    if (destinationProfile === HOST_WRITE_PROFILE_MOVEIT_TRANSFER || destinationProfile === HOST_WRITE_PROFILE_FTP || destinationProfile === HOST_WRITE_PROFILE_SFTP || destinationProfile === HOST_WRITE_PROFILE_S3 || destinationProfile === HOST_WRITE_PROFILE_AZURE_BLOB) {
                        const hostLabel = destinationProfile === HOST_WRITE_PROFILE_FTP ? 'FTP/FTPS' : destinationProfile === HOST_WRITE_PROFILE_SFTP ? 'SFTP' : destinationProfile === HOST_WRITE_PROFILE_S3 ? 'Amazon S3' : destinationProfile === HOST_WRITE_PROFILE_AZURE_BLOB ? 'Azure Blob' : 'MOVEit Transfer';
                        if (destination.overrideCredentials) {
                            if (!String(destination.username || '').trim()) return `${hostLabel} Destination ${destinationProfile === HOST_WRITE_PROFILE_S3 ? 'Access Key ID' : destinationProfile === HOST_WRITE_PROFILE_AZURE_BLOB ? 'Storage Account' : 'Username'} is required when default credentials are overridden.`;
                            if (!destination.passwordConfigured && !String(destination.password || '').length) return `Set the ${hostLabel} Destination ${destinationProfile === HOST_WRITE_PROFILE_S3 ? 'Secret Access Key' : destinationProfile === HOST_WRITE_PROFILE_AZURE_BLOB ? 'Key' : 'Password'} when default credentials are overridden.`;
                        }
                        if (destinationProfile !== HOST_WRITE_PROFILE_SFTP && destinationProfile !== HOST_WRITE_PROFILE_S3 && destinationProfile !== HOST_WRITE_PROFILE_AZURE_BLOB && destination.overrideClientCertificate && String(destination.clientCertificateSha1 || '').trim()
                            && (!String(destination.clientCertificateStore || '').trim() || !String(destination.clientCertificateSubject || '').trim()))
                            return `Select a complete ${hostLabel} Destination client certificate.`;
                        if (destinationProfile === HOST_WRITE_PROFILE_SFTP && destination.overrideClientKey && String(destination.clientKeyId || '').trim()) {
                            const keyId = Number(destination.clientKeyId);
                            if (!Number.isInteger(keyId) || keyId < 0) return 'SFTP Destination Client Key is invalid.';
                        }
                        if (destination.overrideConnectTimeout) { const error = numericOverrideError(destination.connectTimeoutSeconds, `${hostLabel} Destination Connect Timeout`); if (error) return error; }
                        if (destinationProfile !== HOST_WRITE_PROFILE_SFTP && destination.overrideDataTimeout) { const error = numericOverrideError(destination.dataTimeoutSeconds, `${hostLabel} Destination Data Timeout`); if (error) return error; }
                    }
                    if (destinationProfile === HOST_WRITE_PROFILE_FTP
                        && destination.overrideTransferType
                        && !['ascii', 'binary'].includes(String(destination.transferType || '').toLowerCase()))
                        return 'FTP/FTPS Destination Transfer Type must be ASCII or Binary.';
                    if (destinationProfile === HOST_WRITE_PROFILE_SFTP && destination.overrideFileAttributes && destination.setFileAttributes) {
                        if (!/^[0-7]{3}$/.test(String(destination.destinationFileAttributes || '').trim()))
                            return 'SFTP Destination File Attributes must be a 3-digit octal permission such as 644 or 635.';
                    }
                    if (destinationProfile === HOST_WRITE_PROFILE_S3 && !destination.useDefaultBucket && !String(destination.bucket || '').trim())
                        return 'Amazon S3 Destination Bucket Name is required when the Host default bucket is overridden.';
                    if (destinationProfile === HOST_WRITE_PROFILE_AZURE_BLOB && !destination.useDefaultContainer && !String(destination.container || '').trim())
                        return 'Azure Blob Destination Container is required when the Host default container is overridden.';
                    if (destinationProfile === HOST_WRITE_PROFILE_SMTP) {
                        if (!String(destination.addressTo || '').trim()) return 'SMTP Destination Recipients are required.';
                        if (destination.overrideCredentials) {
                            if (Boolean(destination.emailUseOAuth)) {
                                if (!String(destination.emailOAuthUsername || '').trim()) return 'SMTP Destination OAuth Username is required.';
                                if (!String(destination.emailOAuthClientId || '').trim()) return 'SMTP Destination OAuth Client ID is required.';
                                if (!destination.emailOAuthClientSecretConfigured && !String(destination.emailOAuthClientSecret || '').length) return 'Set the SMTP Destination OAuth Client Secret.';
                                if (String(destination.emailOAuthType || 'microsoft365').toLowerCase() === 'microsoft365') {
                                    if (!String(destination.emailOAuthTenantId || '').trim()) return 'SMTP Destination OAuth Tenant ID is required for Microsoft Office 365.';
                                } else {
                                    if (!String(destination.emailOAuthScope || '').trim()) return 'SMTP Destination OAuth Authorization Scope is required for Custom authentication.';
                                    if (!String(destination.emailOAuthTokenUrl || '').trim()) return 'SMTP Destination OAuth Token URL is required for Custom authentication.';
                                }
                            } else {
                                if (!String(destination.username || '').trim()) return 'SMTP Destination Username is required when default credentials are overridden.';
                                if (!destination.passwordConfigured && !String(destination.password || '').length) return 'Set the SMTP Destination Password when default credentials are overridden.';
                            }
                        }
                        if (destination.overrideConnectTimeout) { const error = numericOverrideError(destination.connectTimeoutSeconds, 'SMTP Destination Connect Timeout'); if (error) return error; }
                        if (destination.overrideDataTimeout) { const error = numericOverrideError(destination.dataTimeoutSeconds, 'SMTP Destination Data Timeout'); if (error) return error; }
                    }
                    if (destinationProfile === HOST_WRITE_PROFILE_GOOGLE_STORAGE) {
                        if (!destination.useDefaultBucket && !String(destination.bucket || '').trim())
                            return 'Google Cloud Storage Destination Bucket Name is required when the Host default bucket is overridden.';
                        if (destination.overrideCredentials) {
                            if (Boolean(destination.googleUseOAuth)) {
                                if (!String(destination.googleOAuthClientId || '').trim()) return 'Google Cloud Storage Destination OAuth Client ID is required.';
                                if (!String(destination.googleProjectId || '').trim()) return 'Google Cloud Storage Destination OAuth Project ID is required.';
                                if (!destination.googleOAuthClientSecretConfigured && !String(destination.googleOAuthClientSecret || '').length) return 'Set the Google Cloud Storage Destination OAuth private key.';
                            } else {
                                if (!String(destination.username || '').trim()) return 'Google Cloud Storage Destination Access Key ID is required.';
                                if (!destination.passwordConfigured && !String(destination.password || '').length) return 'Set the Google Cloud Storage Destination Secret Access Key.';
                            }
                        }
                        if (destination.overrideConnectTimeout) { const error = numericOverrideError(destination.connectTimeoutSeconds, 'Google Cloud Storage Destination Connect Timeout'); if (error) return error; }
                        if (destination.overrideDataTimeout) { const error = numericOverrideError(destination.dataTimeoutSeconds, 'Google Cloud Storage Destination Data Timeout'); if (error) return error; }
                    }
                    if (destinationProfile === HOST_WRITE_PROFILE_SHAREPOINT) {
                        if (!destination.useDefaultDocumentLibrary) {
                            if (!String(destination.documentLibrary || '').trim()) return 'SharePoint Destination Document Library is required when the Host default library is overridden.';
                            if (!String(destination.documentLibraryGuid || '').trim()) return 'Select the SharePoint Destination Document Library using Browse so its library ID is known.';
                        }
                        if (destination.overrideCredentials) {
                            const mode = Number(destination.sharePointAuthenticationMode ?? 2);
                            if (mode === 1) {
                                if (!String(destination.sharePointClientId || '').trim()) return 'SharePoint Destination Client ID is required for App Access.';
                                if (!String(destination.sharePointRealmId || '').trim()) return 'SharePoint Destination Realm ID is required for App Access.';
                                if (!destination.sharePointClientSecretConfigured && !String(destination.sharePointClientSecret || '').length) return 'Set the SharePoint Destination Client Secret for App Access.';
                            } else if (mode === 2) {
                                if (!String(destination.sharePointClientId || '').trim()) return 'SharePoint Destination Client ID is required for Certificate Access.';
                                if (!String(destination.sharePointTenantId || '').trim()) return 'SharePoint Destination Tenant ID is required for Certificate Access.';
                                if (!String(destination.sharePointCertificateSha1 || '').trim()) return 'Select a SharePoint Destination Certificate for Certificate Access.';
                            } else return 'Select a valid SharePoint Destination authentication method.';
                        }
                        if (destination.overrideConnectTimeout) { const error = numericOverrideError(destination.connectTimeoutSeconds, 'SharePoint Destination Connect Timeout'); if (error) return error; }
                        if (destination.overrideDataTimeout) { const error = numericOverrideError(destination.dataTimeoutSeconds, 'SharePoint Destination Data Timeout'); if (error) return error; }
                    }
                }
            }
            for (const step of request.steps) {
                if (!step.patch) continue;
                if ((step.kind === 'Source' || step.kind === 'Destination') && !canWriteHostStep(step.kind, step.patch.Type)) {
                    return `The MAUI Task Editor does not yet have a verified write profile for ${hostTypeLabel(step.patch.Type)} ${step.kind} steps.`;
                }
                if ((step.kind === 'Source' || step.kind === 'Destination') && !String(step.patch.HostID || '').trim()) return `${step.kind} Host is required.`;
                if (step.kind === 'Source') {
                    const sourceModel = taskSteps[Number(step.index)]?.source
                        || (String(step.id || '').trim()
                            ? taskSteps.find(item => item.kind === 'Source' && String(item.id || item.source?.id || '') === String(step.id || ''))?.source
                            : null);
                    if (sourceModel?.hasSpecialFilters && !sourceModel.specialFilters?.hasUnsupportedCriteria) {
                        const filters = sourceModel.specialFilters?.filters || [];
                        if (!filters.length) return 'Special Filters requires at least one filter criterion.';
                    }
                }
                if (step.kind === 'Process' && !String(step.patch.ScriptID || '').trim()) return 'Process Script is required.';
            }
            if (!request.task.useGlobalTaskLogRetention && (request.task.taskLogsToKeep == null || request.task.taskLogsToKeep < 0)) return 'Number of Task Logs Saved must be zero or greater.';
            if (!request.task.useDefaultStateCaching && request.task.stateCacheTime == null) return 'Select how long task state information should be cached.';
            const scheduleValidationError = validateScheduleStateForSave();
            if (scheduleValidationError) return scheduleValidationError;
            for (const action of request.nextActions) {
                const type = String(action.type || '').toLowerCase();
                if (type === 'email' && (!String(action.hostId || '').trim() || !String(action.addressTo || '').trim())) return 'Email Next Actions require an SMTP Host and at least one recipient.';
                if (type === 'task' && !String(action.taskId || '').trim()) return 'Run Task Next Actions require a Task.';
                if (type === 'task' && String(action.taskId || '') === String(activeTaskId)) return 'A Run Task Next Action cannot run the current task.';
            }
            return '';
        }

        function editorStateSignature() {
            const fields = editorFields().map(control => [control.dataset.editorField || '', readControl(control)]);
            return JSON.stringify({
                fields,
                steps: stepsComparable(),
                schedules: schedulesComparable(),
                advancedFlow: isAdvancedTask() ? advancedEditor?.getDraft() : null,
                nextActions: nextActionsComparable(),
                taskParameters: taskParametersComparable()
            });
        }

        function captureUndoRefreshState() {
            if (!lastUndo) return null;
            const undo = deepClone(lastUndo);
            let selector = null;
            if (undo.stepKey) {
                const step = taskSteps.find(item => item.key === undo.stepKey);
                if (step) selector = { type: 'step', id: step.id || '', index: step.index, kind: step.kind };
            } else if (undo.scheduleKey) {
                const schedule = schedules.find(item => item.key === undo.scheduleKey);
                if (schedule) selector = { type: 'schedule', id: schedule.id || '', index: schedule.index };
            } else if (undo.actionKey) {
                const action = nextActions.find(item => item.key === undo.actionKey);
                if (action) selector = { type: 'action', id: action.id || '', index: action.index };
            }
            return { undo, selector };
        }

        function restoreUndoRefreshState(state) {
            if (!state?.undo) return;
            lastUndo = state.undo;
            if (state.selector?.type === 'step' && lastUndo.stepKey) {
                const match = taskSteps.find(item => state.selector.id && item.id === state.selector.id)
                    || taskSteps.find(item => item.index === state.selector.index && item.kind === state.selector.kind);
                if (match) lastUndo.stepKey = match.key;
            } else if (state.selector?.type === 'schedule' && lastUndo.scheduleKey) {
                const match = schedules.find(item => state.selector.id && item.id === state.selector.id)
                    || schedules.find(item => item.index === state.selector.index);
                if (match) lastUndo.scheduleKey = match.key;
            } else if (state.selector?.type === 'action' && lastUndo.actionKey) {
                const match = nextActions.find(item => state.selector.id && item.id === state.selector.id)
                    || nextActions.find(item => item.index === state.selector.index);
                if (match) lastUndo.actionKey = match.key;
            }
            updateUndoState();
        }

        function captureEditorSelection() {
            const step = activeStep();
            const schedule = activeSchedule();
            const action = activeNextAction();
            return {
                advancedSelection: isAdvancedTask() ? advancedEditor?.capture() : null,
                tab: tabs.find(tab => tab.classList.contains('active'))?.dataset.traditionalTaskEditorTab || 'task',
                workspace: railSections.find(section => section.classList.contains('active'))?.dataset.traditionalTaskEditorSection || 'steps',
                step: step ? { id: step.id || '', index: step.index, kind: step.kind } : null,
                schedule: schedule ? { id: schedule.id || '', index: schedule.index } : null,
                action: action ? { id: action.id || '', index: action.index } : null
            };
        }

        function restoreEditorSelection(selection) {
            if (!selection) return;
            setTab(selection.tab || 'task', false);
            setWorkspace(selection.workspace || 'steps');
            if (isAdvancedTask() && selection.advancedSelection && selection.workspace !== 'schedules') { advancedEditor?.restore(selection.advancedSelection); return; }

            if (selection.workspace === 'steps' && selection.step) {
                const step = taskSteps.find(item => selection.step.id && item.id === selection.step.id)
                    || taskSteps.find(item => item.index === selection.step.index && item.kind === selection.step.kind);
                if (step) selectStep(step.key);
            } else if (selection.workspace === 'schedules' && selection.schedule) {
                const schedule = schedules.find(item => selection.schedule.id && item.id === selection.schedule.id)
                    || schedules.find(item => item.index === selection.schedule.index);
                if (schedule) selectSchedule(schedule.key);
            } else if (selection.workspace === 'next-actions' && selection.action) {
                const action = nextActions.find(item => selection.action.id && item.id === selection.action.id)
                    || nextActions.find(item => item.index === selection.action.index);
                if (action) selectNextAction(action.key);
            }
        }

        async function reloadAuthoritativeEditor(expectedSignature) {
            const shellEndpoint = endpoint('traditionalTaskEditorShellUrl');
            if (!shellEndpoint) throw new Error('The Traditional Task Editor refresh endpoint is not configured.');
            const selection = captureEditorSelection();
            const undoState = captureUndoRefreshState();
            const shellUrl = new URL(shellEndpoint, window.location.href);
            shellUrl.searchParams.set('id', activeTaskId);
            const refreshed = await fetchJson(shellUrl.toString());

            // Never overwrite a newer edit that occurred while the authoritative refresh was in flight.
            if (editorStateSignature() !== expectedSignature) return false;
            populate(refreshed || {});
            restoreEditorSelection(selection);
            restoreUndoRefreshState(undoState);
            return true;
        }

        // Reconciliation is an explicit read, never a repeated write or forced checkout.
        // Fetch before replacing anything so a failed reload preserves the complete draft.
        async function reloadAfterUncertainSave() {
            if ((!saveOutcomeUncertain && !checkout?.lost) || saving || syncActionBusy) return false;
            const signature = editorStateSignature();
            const confirmed = await window.mauiConfirm?.({ title: 'Reload Task?',
                message: 'Load the current Task from MOVEit? Any unsaved changes in this editor will be discarded.',
                confirmText: 'Reload Task', danger: true });
            if (!confirmed || saving || syncActionBusy || signature !== editorStateSignature()) return false;
            saving = true;
            content.inert = true;
            updateDirtyState();
            try {
                if (checkout?.lost || checkoutConflict) await checkout.open(activeTaskId, true, Boolean(checkout.id) || checkout.lost);
                if (!await reloadAuthoritativeEditor(signature)) throw new Error('The draft changed while reloading.');
                saveOutcomeUncertain = false; checkoutConflict = false; manualRecoverySave = false;
                footerSaveError = '';
                autoSaveBlockedSignature = '';
                lastUndo = null;
                updateUndoState();
                return true;
            } catch (reloadError) {
                footerSaveError = `Unable to reload the Task. Your unsaved changes remain in this editor. ${reloadError?.message || 'Try Reload Task again.'}`;
                return false;
            } finally {
                saving = false;
                content.inert = false;
                updateDirtyState();
            }
        }

        async function performSave(options = {}) {
            if (checkout?.lost) { setFooterStatus('The editing checkout was lost. Saving is blocked. Use Reload Task or, for MOVEit administrators, Override Checkout.', 'error'); return false; }
            if (saveOutcomeUncertain) { setFooterStatus('Use Reload Task to confirm its saved state before making another save.', 'error'); return false; }
            const automatic = options.automatic === true;
            let requestSignature = editorStateSignature();
            const request = buildSaveRequest();
            request.editVersion = editVersion;
            request.captureUndo = !serverUndoCaptured;
            const validationError = validateSaveRequest(request);
            if (validationError) {
                footerSaveError = validationError;
                autoSaveBlockedSignature = requestSignature;
                updateDirtyState();
                return false;
            }
            if (request.syncSettings && ['direction','deletePolicy','caseSensitive'].some(key => key in request.syncSettings)) {
                const confirmed = await window.mauiConfirm?.({
                    title: 'Save Sync changes?',
                    message: 'Saving this change will clear the sync listing and disable the Task. Files and folders may be transferred again. Review a Sync Preview before enabling it. Initial Run options can avoid re-transferring unchanged files.',
                    confirmText: 'Save and Disable Task'
                });
                if (!confirmed) { autoSaveBlockedSignature = requestSignature; updateDirtyState(); return false; }
                // Build the request again after confirmation so edits made while the dialog was open
                // cannot send an older draft or bypass confirmation of a newer policy.
                if (editorStateSignature() !== requestSignature) { autoSaveBlockedSignature = editorStateSignature(); updateDirtyState(); return false; }
                request.syncChangesConfirmed = true;
                request.task.enabled = false;
                const enabled = dialog.querySelector('[data-editor-field="enabled"]');
                if (enabled) { enabled.checked = false; commitField(enabled); }
                requestSignature = editorStateSignature();
            }
            const baseUrl = endpoint('traditionalTaskEditorSaveUrl');
            if (!baseUrl) {
                footerSaveError = 'The Traditional Task Editor save endpoint is not configured.';
                autoSaveBlockedSignature = requestSignature;
                updateDirtyState();
                return false;
            }

            window.clearTimeout(autoSaveTimer);
            autoSaveTimer = 0;
            footerSaveError = '';
            saving = true;
            // Reconcile server-assigned step/schedule IDs before accepting another edit.
            content.inert = true;
            closeActionsMenu();
            updateDirtyState();
            try {
                const url = new URL(baseUrl, window.location.href);
                url.searchParams.set('id', activeTaskId);
                const saved = await fetchJson(url.toString(), { method: 'POST', body: JSON.stringify(request) });
                if (saved?.success !== true || String(saved.taskId || '') !== activeTaskId || !String(saved.editVersion || '').trim()) {
                    throw new Error('The Task save response could not be confirmed. Reload the Task before saving again.');
                }
                editVersion = String(saved.editVersion);
                if (request.captureUndo) serverUndoCaptured = true;
                autoSaveBlockedSignature = '';

                // All Task editors wait for the authoritative baseline, including IDs assigned
                // to new steps/schedules. Never queue a second write against unreconciled IDs.
                try {
                    if (!await reloadAuthoritativeEditor(requestSignature)) throw new Error('The draft changed during save reconciliation.');
                } catch (refreshError) {
                    saveOutcomeUncertain = true;
                    autoSaveBlockedSignature = editorStateSignature();
                    footerSaveError = `Task was saved, but the editor could not refresh MOVEit's authoritative state: ${refreshError?.message || 'refresh failed'}. Saving is blocked. Use Reload Task to load the saved copy; unsaved changes in this editor will be discarded.`;
                    setFooterStatus(footerSaveError, 'error');
                    return false;
                }

                footerSaveError = ''; checkoutConflict = false; manualRecoverySave = false;
                return true;
            } catch (saveError) {
                autoSaveBlockedSignature = requestSignature;
                if (saveError?.payload?.checkoutConflict || saveError?.payload?.editConflict || saveError?.payload?.moveItSaved || !saveError?.status || saveError.status >= 500) {
                    saveOutcomeUncertain = true;
                    if (request.captureUndo) serverUndoCaptured = true;
                }
                footerSaveError = saveError?.message || 'Unable to save the Task.';
                checkoutConflict = Boolean(saveError?.payload?.checkoutConflict || saveError?.payload?.editConflict && saveError?.payload?.moveItSaved === false);
                if (saveOutcomeUncertain) footerSaveError += ' ' + (window.MauiObjectEditLease?.blockedMessage?.('Task') || 'Saving is blocked. Reload Task to discard unsaved changes and load the current MOVEit copy.');
                setFooterStatus(footerSaveError, 'error');
                return false;
            } finally {
                saving = false;
                content.inert = false;
                updateDirtyState();
                if (!footerSaveError && !dirty) {
                    setFooterStatus(automatic ? 'Auto Save complete.' : 'Saved.');
                    window.setTimeout(() => { if (!dirty && !saving && !footerSaveError) updateDirtyState(); }, 2200);
                }
            }
        }

        async function saveEditor(options = {}) {
            const automatic = options.automatic === true;
            if (editingHost) return false;
            if ((!dirty && !options.force) || !activeTaskId) return !dirty;
            if (!canAlterTask) { footerSaveError = 'The current MOVEit user does not have permission to alter this Task.'; updateDirtyState(); return false; }

            if (activeSavePromise) {
                const priorSucceeded = await activeSavePromise;
                if (!priorSucceeded && automatic && autoSaveBlockedSignature === editorStateSignature()) return false;
                if (!dirty) return true;
                return await saveEditor(options);
            }

            const operation = performSave(options);
            activeSavePromise = operation;
            try {
                return await operation;
            } finally {
                if (activeSavePromise === operation) activeSavePromise = null;
                if (autoSave && dirty && !footerSaveError) scheduleAutoSave();
            }
        }

        async function flushPendingSave() {
            window.clearTimeout(autoSaveTimer);
            autoSaveTimer = 0;

            // Wait for an in-flight Auto Save instead of treating "saving" as success. If the user
            // changed the Task during that request, immediately persist the newer revision as well.
            for (let attempt = 0; attempt < 10; attempt += 1) {
                if (activeSavePromise) {
                    const succeeded = await activeSavePromise;
                    if (!succeeded && autoSaveBlockedSignature === editorStateSignature()) return false;
                }
                if (!dirty) return true;
                const succeeded = await saveEditor({ automatic: false });
                if (!succeeded) return false;
            }

            footerSaveError = 'The Task kept changing while MAUI was trying to save it. Finish the current edit and try again.';
            updateDirtyState();
            return false;
        }

        function syncPreviewError(message) {
            const element = syncPreviewDialog?.querySelector('[data-sync-preview-error]');
            if (element) { element.textContent = message || ''; element.hidden = !message; }
        }

        function updateSyncPreviewButtons() {
            if (!syncPreviewDialog) return;
            const blocked = syncPreviewBusy || syncActionBusy || saveOutcomeUncertain;
            syncPreviewDialog.querySelector('[data-sync-preview-refresh]').disabled = syncPreviewBusy || syncActionBusy;
            syncPreviewDialog.querySelector('[data-sync-preview-ok]').disabled = blocked || !syncPreviewData;
            const initial = syncPreviewDialog.querySelector('[data-sync-preview-initial]');
            initial.hidden = !syncPreviewData?.canChangeInitialRunOptions;
            initial.disabled = blocked || !canAlterTask;
            syncPreviewDialog.querySelector('[data-sync-preview-loading]').hidden = !syncPreviewBusy;
        }

        function renderSyncPreview() {
            if (!syncPreviewDialog) return;
            const rows = syncPreviewDialog.querySelector('[data-sync-preview-rows]'); rows.replaceChildren();
            const showNoAction = syncPreviewDialog.querySelector('[data-sync-preview-show-no-action]').checked;
            const entries = (syncPreviewData?.entries || []).filter(entry => showNoAction || !entry.noAction);
            const fragment = document.createDocumentFragment();
            const sizeText = value => {
                if (value == null || value === '') return '';
                const amount = Number(value);
                if (!Number.isFinite(amount) || amount < 0) return String(value);
                if (amount < 1024) return `${amount} B`;
                const unit = Math.min(4, Math.floor(Math.log(amount) / Math.log(1024)));
                return `${(amount / (1024 ** unit)).toFixed(1)} ${['B','KB','MB','GB','TB'][unit]}`;
            };
            for (const entry of entries) {
                const row = document.createElement('tr');
                for (const value of [entry.folderA?.path, sizeText(entry.folderA?.size), entry.folderA?.timestamp,
                    entry.action, entry.folderB?.path, sizeText(entry.folderB?.size), entry.folderB?.timestamp]) {
                    const cell = document.createElement('td'); cell.textContent = value || ''; row.appendChild(cell);
                }
                fragment.appendChild(row);
            }
            rows.appendChild(fragment);
            syncPreviewDialog.querySelector('[data-sync-preview-empty]').hidden = !syncPreviewData || entries.length > 0;
            syncPreviewDialog.querySelector('[data-sync-preview-status]').textContent = syncPreviewData
                ? `Showing ${entries.length} of ${syncPreviewData.totalEntries} entries.${syncPreviewData.truncated ? ' Display limited to 1,000 entries.' : ''}` : '';
            updateSyncPreviewButtons();
        }

        async function refreshSyncPreview() {
            if (!syncPreviewDialog?.open || !isSyncTask() || syncActionBusy) return false;
            syncPreviewRequest?.abort();
            const sequence = ++syncPreviewSequence, id = activeTaskId;
            const request = new AbortController(); syncPreviewRequest = request;
            syncPreviewBusy = true; syncPreviewData = null; syncPreviewError(''); renderSyncPreview();
            try {
                const url = new URL(endpoint('syncPreviewUrl'), window.location.href); url.searchParams.set('id', id);
                const data = await fetchJson(url.toString(), { signal: request.signal });
                if (sequence !== syncPreviewSequence || id !== activeTaskId || !syncPreviewDialog.open) return false;
                if (!Array.isArray(data?.entries) || typeof data?.canChangeInitialRunOptions !== 'boolean') throw new Error('MOVEit did not return a valid Sync Preview.');
                syncPreviewData = data;
                renderSyncPreview(); return true;
            } catch (error) {
                if (sequence === syncPreviewSequence && error?.name !== 'AbortError') syncPreviewError(error?.message || 'Unable to load Sync Preview.');
                return false;
            } finally {
                if (sequence === syncPreviewSequence) { syncPreviewBusy = false; syncPreviewRequest = null; updateSyncPreviewButtons(); }
            }
        }

        async function openSyncPreview() {
            closeActionsMenu();
            if (!isSyncTask() || syncActionBusy || saveOutcomeUncertain || !syncPreviewDialog) return false;
            if (!await flushPendingSave()) return false;
            syncPreviewDialog.querySelector('[data-sync-preview-task-name]').textContent = activeTaskName;
            syncPreviewDialog.querySelector('[data-sync-preview-show-no-action]').checked = false;
            if (!syncPreviewDialog.open) syncPreviewDialog.showModal();
            return await refreshSyncPreview();
        }

        function closeSyncPreview() {
            if (syncActionBusy) return;
            syncPreviewRequest?.abort(); syncPreviewSequence++; syncPreviewBusy = false;
            syncPreviewDialog?.close();
        }

        async function postSyncAction(action) {
            if (!isSyncTask() || !canAlterTask || syncActionBusy || saveOutcomeUncertain) return false;
            const id = activeTaskId, signature = editorStateSignature();
            syncActionBusy = true; saving = true; dialog.inert = true;
            updateDirtyState(); updateSyncPreviewButtons();
            let dispatched = false;
            try {
                const url = new URL(endpoint('syncActionUrl'), window.location.href); url.searchParams.set('id', id);
                dispatched = true;
                const result = await fetchJson(url.toString(), { method:'POST', body:JSON.stringify({action,confirmed:true,editVersion}) });
                if (result?.success !== true || String(result?.taskId) !== id) {
                    const error = new Error('The Sync action acknowledgement could not be confirmed. Reopen the Task before retrying.');
                    error.payload = {moveItSaved:true}; throw error;
                }
                if (id !== activeTaskId || !await reloadAuthoritativeEditor(signature)) {
                    const error = new Error('The Sync action completed, but the editor could not refresh. Reopen the Task before continuing.');
                    error.payload = {moveItSaved:true}; throw error;
                }
                lastUndo = null; updateUndoState(); footerSaveError = '';
                return true;
            } catch (error) {
                if (dispatched && (error?.payload?.editConflict || error?.payload?.moveItSaved || !error?.status || error.status >= 500)) saveOutcomeUncertain = true;
                footerSaveError = error?.message || 'Unable to complete the Sync action.';
                if (saveOutcomeUncertain && !/reopen/i.test(footerSaveError)) footerSaveError += ' Reopen the Task before retrying.';
                syncPreviewError(footerSaveError); return false;
            } finally {
                syncActionBusy = false; saving = false; dialog.inert = false; updateDirtyState(); updateSyncPreviewButtons();
            }
        }

        async function runSyncAction(action) {
            closeActionsMenu();
            if (!isSyncTask() || !canAlterTask || syncActionBusy || saveOutcomeUncertain) return;
            if (!await flushPendingSave()) return;
            const clear = action === 'clear-listings';
            const confirmed = await window.mauiConfirm?.({
                title: clear ? 'Clear Sync Listings?' : 'Swap Sync Folders?',
                message: clear ? `Clear the saved Sync listings for "${activeTaskName}"? The Task will be disabled and treated as a new Sync Task. Files and folders may be transferred again. Review a Sync Preview before enabling it; Initial Run options can avoid re-transferring unchanged files.`
                    : `Swap Folder A and Folder B for "${activeTaskName}"? Their Hosts, paths and overrides move together. The Sync direction and deletion policy are retained.`,
                confirmText: clear ? 'Clear Sync Listings' : 'Swap Sync Folders'
            });
            if (confirmed && await postSyncAction(action) && clear) await openSyncPreview();
        }

        function openSyncInitialOptions() {
            if (!syncInitialDialog || !canAlterTask || syncActionBusy || !syncPreviewData?.canChangeInitialRunOptions) return;
            const mode = dialog.querySelector('[data-editor-field="syncInitialRun"]').value;
            const seconds = Number(dialog.querySelector('[data-editor-field="syncSameSeconds"]').value || 0);
            const unit = [86400,3600,60].find(value => seconds > 0 && seconds % value === 0) || 1;
            syncInitialDialog.querySelector(`[name="maui-sync-initial-mode"][value="${mode}"]`).checked = true;
            syncInitialDialog.querySelector('[data-sync-initial-value]').value = String(seconds / unit);
            syncInitialDialog.querySelector('[data-sync-initial-unit]').value = String(unit);
            syncInitialDialog.querySelector('[data-sync-initial-error]').hidden = true;
            refreshSyncInitialOptions(); syncInitialDialog.showModal();
        }
        function refreshSyncInitialOptions() {
            const custom = syncInitialDialog.querySelector('[name="maui-sync-initial-mode"]:checked')?.value === 'custom';
            syncInitialDialog.querySelectorAll('[data-sync-initial-timespan] input,[data-sync-initial-timespan] select').forEach(control => control.disabled = !custom);
        }
        async function saveSyncInitialOptions() {
            if (!canAlterTask || saving || syncActionBusy || saveOutcomeUncertain) return;
            const mode = syncInitialDialog.querySelector('[name="maui-sync-initial-mode"]:checked')?.value;
            const value = syncInitialDialog.querySelector('[data-sync-initial-value]').value.trim();
            const seconds = Number(value) * Number(syncInitialDialog.querySelector('[data-sync-initial-unit]').value);
            if (!mode || mode === 'custom' && (!value || !Number.isInteger(Number(value)) || !Number.isInteger(seconds) || seconds < 0 || seconds > 2147483647)) {
                const error = syncInitialDialog.querySelector('[data-sync-initial-error]'); error.textContent = 'Enter a whole timespan from 0 to 2,147,483,647 seconds.'; error.hidden = false; return;
            }
            const field = dialog.querySelector('[data-editor-field="syncInitialRun"]'); writeControl(field, mode); commitField(field);
            if (mode === 'custom') { const tolerance = dialog.querySelector('[data-editor-field="syncSameSeconds"]'); writeControl(tolerance, String(seconds)); commitField(tolerance); }
            const button = syncInitialDialog.querySelector('[data-sync-initial-save]'); button.disabled = true;
            syncInitialDialog.inert = true;
            try {
                if (await flushPendingSave()) { syncInitialDialog.close(); await refreshSyncPreview(); }
                else { const error = syncInitialDialog.querySelector('[data-sync-initial-error]'); error.textContent = footerSaveError || 'The Initial Run options could not be saved.'; error.hidden = false; }
            }
            finally { button.disabled = saveOutcomeUncertain; syncInitialDialog.inert = false; }
        }

        async function runSavedTaskAction(callback) {
            closeActionsMenu();
            if (typeof callback !== 'function') return;
            const saved = await flushPendingSave();
            if (!saved) return;
            return await callback(activeTaskId, activeTaskName);
        }

        function addSource() {
            if (isSyncTask() && taskSteps.length >= 2) return;
            closeAddStepMenu();
            const key = `local-source:${++temporaryStepSequence}`;
            const source = normalizeSource({
                hostId: '', hostName: '', hostTypeCode: '', hostType: '', path: '', fileMask: '*.*', excludeFolders: '', searchSubdirectories: false,
                excludeFiles: '', hasSpecialFilters: false, specialFilters: null, collectOnlyNewFiles: false, expandZipFiles: false, retryIfNoFilesFound: false,
                deleteOriginalAction: 0, renameTo: '', renamedFilesMayOverwrite: false,
                useDefaultRetryCount: true, useDefaultRetryTimeout: true, useDefaultRescanTime: true,
                useDefaultMaxFiles: true, useDefaultMaxBytes: true, useDefaultWindowsCopyFileApi: true, hostDefaults: {}
            });
            const firstNonSource = taskSteps.findIndex(step => step.kind !== 'Source');
            const position = firstNonSource < 0 ? taskSteps.length : firstNonSource;
            const step = { key, index: position, kind: 'Source', id: '', title: 'Source', subtitle: 'Select a Host', source, process: null, destination: null };
            taskSteps.splice(position, 0, step);
            normalizeStepIndexes();
            if (isAdvancedTask()) advancedEditor?.attach(step, 'Source');
            seedSourceStableValues(step);
            if (!isAdvancedTask()) lastUndo = { kind: 'step-add', stepKey: key, label: 'Add Source' };
            activeStepKey = key;
            renderStepRail();
            selectStep(key);
            setSourceTab('general');
            updateUndoState();
            updateDirtyState();
            sourceHostSelect?.focus();
        }


        function addProcess() {
            if (isSyncTask()) return;
            closeAddStepMenu();
            const key = `local-process:${++temporaryStepSequence}`;
            const process = normalizeProcess({ scriptId: '', scriptName: '', scriptType: '', run: 'PerFile', isDestination: false, parameters: [] });
            const firstDestination = taskSteps.findIndex(step => step.kind === 'Destination');
            const position = firstDestination < 0 ? taskSteps.length : firstDestination;
            const step = { key, index: position, kind: 'Process', id: '', title: 'Process', subtitle: 'Select a Script', source: null, process, destination: null };
            taskSteps.splice(position, 0, step);
            normalizeStepIndexes();
            if (isAdvancedTask()) advancedEditor?.attach(step, 'Process');
            seedProcessStableValues(step);
            if (!isAdvancedTask()) lastUndo = { kind: 'step-add', stepKey: key, label: 'Add Process' };
            activeStepKey = key;
            renderStepRail();
            selectStep(key);
            setProcessTab('general');
            updateUndoState();
            updateDirtyState();
            processScriptSelect?.focus();
        }

        function addDestination() {
            if (isSyncTask()) return;
            closeAddStepMenu();
            const key = `local-destination:${++temporaryStepSequence}`;
            const destination = normalizeDestination({
                hostId: '', hostName: '', hostTypeCode: '', hostType: '', path: '', useRelativeSubdirectories: false,
                forceDirectory: false, zip: false, useOriginalName: true, fileName: '', overwriteAction: 1,
                useDefaultRetryCount: true, useDefaultRetryTimeout: true, useDefaultWindowsCopyFileApi: true,
                useDefaultPreserveFileTimestamp: true, hostDefaults: {}
            });
            const position = taskSteps.length;
            const step = { key, index: position, kind: 'Destination', id: '', title: 'Destination', subtitle: 'Select a Host', source: null, process: null, destination };
            taskSteps.push(step);
            normalizeStepOrder();
            if (isAdvancedTask()) advancedEditor?.attach(step, 'Destination');
            seedDestinationStableValues(step);
            if (!isAdvancedTask()) lastUndo = { kind: 'step-add', stepKey: key, label: 'Add Destination' };
            activeStepKey = key;
            renderStepRail();
            selectStep(key);
            setDestinationTab('general');
            updateUndoState();
            updateDirtyState();
            destinationHostSelect?.focus();
        }

        function cloneActiveStep() {
            if (isAdvancedTask()) { advancedEditor?.copy(); return; }
            const step = activeStep();
            if (!step) return;
            const kind = String(step.kind || '').toLowerCase();
            if (kind === 'source' && step.source && !canWriteHostStep('Source', step.source.hostTypeCode)) {
                setFooterStatus(`This ${step.source.hostType || hostTypeLabel(step.source.hostTypeCode)} Source cannot be cloned until MAUI has a verified write profile for that Host type.`, 'error');
                return;
            }
            if (kind === 'destination' && step.destination && !canWriteHostStep('Destination', step.destination.hostTypeCode)) {
                setFooterStatus(`This ${step.destination.hostType || hostTypeLabel(step.destination.hostTypeCode)} Destination cannot be cloned until MAUI has a verified write profile for that Host type.`, 'error');
                return;
            }
            if (kind === 'source' && step.source?.specialFilters?.hasUnsupportedCriteria) {
                setFooterStatus('This Source cannot be cloned safely because it contains Special Filter criteria MAUI does not recognize.', 'error');
                return;
            }
            if (kind === 'process' && (step.process?.parameters || []).some(parameter => parameter.secure && !parameter.valueChanged)) {
                setFooterStatus('This Process cannot be cloned safely because it contains a suppressed secure parameter value that MAUI cannot copy to a new Step.', 'error');
                return;
            }
            const clone = deepClone(step);
            if (!['source', 'process', 'destination'].includes(kind)) {
                setFooterStatus('Clone is not supported for this Step type.', 'error');
                return;
            }

            clone.key = `local-${kind}:${++temporaryStepSequence}`;
            clone.id = '';
            if (clone.source) { clone.source.id = ''; clone.source.serverHostTypeCode = ''; }
            if (clone.process) clone.process.id = '';
            if (clone.destination) { clone.destination.id = ''; clone.destination.serverHostTypeCode = ''; }
            clone.title = step.kind;
            clone.subtitle = stepSummaryText(clone);
            const index = taskSteps.findIndex(item => item.key === step.key);
            const position = index < 0 ? taskSteps.length : index + 1;
            taskSteps.splice(position, 0, clone);
            normalizeStepOrder();
            seedSourceStableValues(clone);
            seedProcessStableValues(clone);
            seedDestinationStableValues(clone);
            lastUndo = { kind: 'step-add', stepKey: clone.key, label: `Clone ${step.kind}` };
            activeStepKey = clone.key;
            renderStepRail();
            selectStep(clone.key);
            updateUndoState();
            updateDirtyState();
            setFooterStatus(`${step.kind} cloned.`);
        }

        function removeActiveStep() {
            if (isAdvancedTask()) { void advancedEditor?.remove(); return; }
            const index = taskSteps.findIndex(step => step.key === activeStepKey);
            if (index < 0) return;
            const removed = deepClone(taskSteps[index]);
            taskSteps.splice(index, 1);
            normalizeStepIndexes();
            lastUndo = { kind: 'step-remove', step: removed, position: index, label: `Remove ${removed.kind}` };
            sourceStableValues.forEach((_, key) => { if (key.startsWith(`${removed.key}|`)) sourceStableValues.delete(key); });
            processStableValues.forEach((_, key) => { if (key.startsWith(`${removed.key}|`)) processStableValues.delete(key); });
            destinationStableValues.forEach((_, key) => { if (key.startsWith(`${removed.key}|`)) destinationStableValues.delete(key); });
            const next = taskSteps[index] || taskSteps[index - 1] || null;
            activeStepKey = next?.key || '';
            renderStepRail();
            if (next) selectStep(next.key); else showNoStep();
            updateUndoState();
            updateDirtyState();
        }

        function normalizeStepIndexes() {
            taskSteps.forEach((step, index) => { step.index = index; });
        }

        function captureBaseline(preserveUndo = false) {
            const preservedUndo = preserveUndo ? lastUndo : null;
            baseline.clear();
            stableValues.clear();
            editorFields().forEach(control => {
                const key = control.dataset.editorField;
                const value = readControl(control);
                baseline.set(key, value);
                stableValues.set(key, value);
            });
            stepsBaseline = stepsComparable();
            stepBaselineModels.clear();
            taskSteps.forEach(step => stepBaselineModels.set(step.key, deepClone({
                kind: step.kind,
                source: step.source,
                process: step.process,
                destination: step.destination
            })));
            schedulesBaseline = schedulesComparable();
            schedulesBaselineHadNativeMulti = schedules.some(schedule => isPairedOvernightSchedule(schedule) || Number(schedule?.multi || 0) !== 0);
            nextActionsBaseline = nextActionsComparable();
            taskParametersBaseline = taskParametersComparable();
            sourceStableValues.clear();
            processStableValues.clear();
            destinationStableValues.clear();
            taskSteps.forEach(step => { seedSourceStableValues(step); seedProcessStableValues(step); seedDestinationStableValues(step); });
            nextActionStableValues.clear();
            nextActions.forEach(seedNextActionStableValues);
            lastUndo = preservedUndo;
            updateUndoState();
            updateDirtyState();
        }

        function editorFields() {
            return Array.from(dialog.querySelectorAll('[data-editor-field]'));
        }

        function readControl(control) {
            if (control?.dataset?.editorField === 'syncCaseSensitive' && control.indeterminate) return 'mixed';
            if (control instanceof HTMLInputElement && control.type === 'checkbox') return control.checked ? 'true' : 'false';
            return String(control?.value ?? '');
        }

        function writeControl(control, value) {
            if (control?.dataset?.editorField === 'syncCaseSensitive') {
                control.indeterminate = value === 'mixed';
                if (value === 'mixed') value = control.dataset.syncMixedChecked;
            }
            if (control instanceof HTMLInputElement && control.type === 'checkbox') control.checked = String(value) === 'true';
            else control.value = value ?? '';
            refreshLinkedPresentation(control);
        }

        function commitField(control) {
            const key = control?.dataset?.editorField;
            if (!key) return;
            const before = stableValues.has(key) ? stableValues.get(key) : readControl(control);
            const after = readControl(control);
            if (before === after) { updateDirtyState(); return; }

            lastUndo = { kind: 'field', key, before, after, label: fieldLabel(control) };
            stableValues.set(key, after);
            updateUndoState();
            updateDirtyState();
        }

        function fieldLabel(control) {
            const caption = control.closest('.field-block')?.querySelector(':scope > span')?.textContent?.trim();
            if (caption) return caption;
            if (control.matches('[data-traditional-task-editor-enabled]')) return 'Task Enabled';
            return control.dataset.editorField || 'change';
        }

        async function requestUndoLastEdit() {
            closeActionsMenu();
            if (!lastUndo) return;
            const confirmed = await window.mauiConfirm?.({
                title: 'Undo Last Change?',
                message: `This will undo the last change: ${lastUndo.label}.`,
                confirmText: 'Undo Change'
            });
            if (confirmed) undoLastEdit();
        }

        function undoLastEdit() {
            if (!lastUndo) return;
            const undo = lastUndo;
            lastUndo = null;

            if (undo.kind === 'advanced-flow') {
                taskSteps=deepClone(undo.steps);nextActions=deepClone(undo.actions);advancedEditor?.restoreDraft(undo.flow,undo.selection);
            } else if (undo.kind === 'field') {
                const control = editorFields().find(item => item.dataset.editorField === undo.key);
                if (control) {
                    writeControl(control, undo.before);
                    stableValues.set(undo.key, undo.before);
                    control.focus?.();
                }
            } else if (undo.kind === 'source-field') {
                const step = taskSteps.find(item => item.key === undo.stepKey);
                if (step?.source) {
                    step.source[undo.field] = cloneSimple(undo.before);
                    seedSourceStableValues(step);
                    activeStepKey = step.key;
                    selectStep(step.key);
                }
            } else if (undo.kind === 'source-snapshot') {
                const step = taskSteps.find(item => item.key === undo.stepKey);
                if (step) {
                    step.source = deepClone(undo.before);
                    seedSourceStableValues(step);
                    activeStepKey = step.key;
                    selectStep(step.key);
                }
            } else if (undo.kind === 'process-field') {
                const step = taskSteps.find(item => item.key === undo.stepKey);
                if (step?.process) {
                    step.process[undo.field] = cloneSimple(undo.before);
                    if (undo.field === 'scriptId') {
                        const script = scriptOptions.find(item => item.id === String(step.process.scriptId));
                        step.process.scriptName = script?.name || '';
                        step.process.scriptType = script?.type || '';
                    }
                    seedProcessStableValues(step);
                    activeStepKey = step.key;
                    selectStep(step.key);
                }
            } else if (undo.kind === 'process-snapshot') {
                const step = taskSteps.find(item => item.key === undo.stepKey);
                if (step) {
                    step.process = deepClone(undo.before);
                    seedProcessStableValues(step);
                    activeStepKey = step.key;
                    selectStep(step.key);
                }
            } else if (undo.kind === 'destination-field') {
                const step = taskSteps.find(item => item.key === undo.stepKey);
                if (step?.destination) {
                    step.destination[undo.field] = cloneSimple(undo.before);
                    seedDestinationStableValues(step);
                    activeStepKey = step.key;
                    selectStep(step.key);
                }
            } else if (undo.kind === 'destination-snapshot') {
                const step = taskSteps.find(item => item.key === undo.stepKey);
                if (step) {
                    step.destination = deepClone(undo.before);
                    seedDestinationStableValues(step);
                    activeStepKey = step.key;
                    selectStep(step.key);
                }
            } else if (undo.kind === 'step-add') {
                const index = taskSteps.findIndex(item => item.key === undo.stepKey);
                if (index >= 0) taskSteps.splice(index, 1);
                normalizeStepIndexes();
                activeStepKey = taskSteps[index]?.key || taskSteps[index - 1]?.key || '';
                renderStepRail();
                if (activeStepKey) selectStep(activeStepKey); else showNoStep();
            } else if (undo.kind === 'step-remove') {
                taskSteps.splice(Math.min(undo.position, taskSteps.length), 0, deepClone(undo.step));
                normalizeStepIndexes();
                const restored = taskSteps.find(item => item.key === undo.step.key);
                if (restored) { seedSourceStableValues(restored); seedProcessStableValues(restored); seedDestinationStableValues(restored); }
                activeStepKey = undo.step.key;
                renderStepRail();
                selectStep(activeStepKey);
            } else if (undo.kind === 'schedule-snapshot') {
                const index = schedules.findIndex(item => item.key === undo.scheduleKey);
                if (index >= 0) {
                    const restored = deepClone(undo.before);
                    restored.key = undo.scheduleKey;
                    schedules[index] = restored;
                    activeScheduleKey = restored.key;
                    renderScheduleRail();
                    selectSchedule(restored.key);
                }
            } else if (undo.kind === 'schedule-add') {
                const index = schedules.findIndex(item => item.key === undo.scheduleKey);
                if (index >= 0) schedules.splice(index, 1);
                normalizeScheduleIndexes();
                activeScheduleKey = schedules[index]?.key || schedules[index - 1]?.key || '';
                renderScheduleRail();
                if (activeScheduleKey) selectSchedule(activeScheduleKey); else showNoSchedule();
            } else if (undo.kind === 'schedule-remove') {
                schedules.splice(Math.min(undo.position, schedules.length), 0, deepClone(undo.schedule));
                normalizeScheduleIndexes();
                const restored = schedules.find(item => item.key === undo.schedule.key);
                activeScheduleKey = undo.schedule.key;
                renderScheduleRail();
                selectSchedule(activeScheduleKey);
            } else if (undo.kind === 'next-action-field') {
                const action = nextActions.find(item => item.key === undo.actionKey);
                if (action) {
                    action[undo.field] = cloneSimple(undo.before);
                    if (undo.field === 'hostId') action.hostName = smtpHostName(action.hostId);
                    if (undo.field === 'taskId') action.taskName = taskOptionName(action.taskId);
                    seedNextActionStableValues(action);
                    activeNextActionKey = action.key;
                    selectNextAction(action.key);
                }
            } else if (undo.kind === 'next-action-snapshot') {
                const index = nextActions.findIndex(item => item.key === undo.actionKey);
                if (index >= 0) {
                    const restored = deepClone(undo.before);
                    restored.key = undo.actionKey;
                    nextActions[index] = restored;
                    seedNextActionStableValues(restored);
                    activeNextActionKey = restored.key;
                    selectNextAction(restored.key);
                }
            } else if (undo.kind === 'next-action-add') {
                const index = nextActions.findIndex(item => item.key === undo.actionKey);
                if (index >= 0) nextActions.splice(index, 1);
                normalizeNextActionIndexes();
                activeNextActionKey = nextActions[index]?.key || nextActions[index - 1]?.key || '';
                renderNextActionRail();
                if (activeNextActionKey) selectNextAction(activeNextActionKey); else showNoNextAction();
            } else if (undo.kind === 'next-action-remove') {
                nextActions.splice(Math.min(undo.position, nextActions.length), 0, deepClone(undo.action));
                normalizeNextActionIndexes();
                const restored = nextActions.find(item => item.key === undo.action.key);
                if (restored) seedNextActionStableValues(restored);
                activeNextActionKey = undo.action.key;
                renderNextActionRail();
                selectNextAction(activeNextActionKey);
            } else if (undo.kind === 'task-parameters-snapshot') {
                taskParameters = deepClone(undo.before);
                renderTaskParameters();
                scriptGuidance?.show(activeStep()?.process);
            }

            setFooterStatus(`Undid ${undo.label}.`);
            updateUndoState();
            updateDirtyState();
        }

        function updateUndoState() {
            if (!undoButton) return;
            undoButton.disabled = !lastUndo;
            undoButton.title = lastUndo ? `Undo ${lastUndo.label}` : 'No editor change is available to undo.';
        }

        function isPreservedUnsupportedHostStep(step) {
            if (!step) return false;
            const baselineStep = stepBaselineModels.get(step.key);
            if (!baselineStep) return false;
            if (step.kind === 'Source' && step.source && !canWriteHostStep('Source', step.source.hostTypeCode)) {
                return sameValue(hostStepConfiguration(step.source), hostStepConfiguration(baselineStep.source));
            }
            if (step.kind === 'Destination' && step.destination && !canWriteHostStep('Destination', step.destination.hostTypeCode)) {
                return sameValue(hostStepConfiguration(step.destination), hostStepConfiguration(baselineStep.destination));
            }
            return true;
        }

        function incompleteHostStep() {
            return taskSteps.find(step => {
                const model = step.kind === 'Source' ? step.source : step.kind === 'Destination' ? step.destination : null;
                if (!model || String(model.hostId ?? '').trim() && String(model.hostTypeCode ?? '').trim()) return false;
                // Existing unrecognized steps may still be preserved unchanged. Only
                // an incomplete editable/new step needs a Host-selection prompt.
                return canWriteHostStep(step.kind, model.hostTypeCode) || !isPreservedUnsupportedHostStep(step);
            });
        }

        function isSupportedHostSavePattern() {
            if (incompleteHostStep()) return false;
            return taskSteps.every(step => {
                if (step.kind === 'Source' && step.source)
                    return canWriteHostStep('Source', step.source.hostTypeCode) || isPreservedUnsupportedHostStep(step);
                if (step.kind === 'Destination' && step.destination)
                    return canWriteHostStep('Destination', step.destination.hostTypeCode) || isPreservedUnsupportedHostStep(step);
                return true;
            });
        }

        function friendlyEditorError(message) {
            let text = String(message || '').trim();
            if (!text) return 'The operation could not be completed.';
            text = text
                .replace(/MOVEit\s+REST(?:\s+API)?\s+(?:request\s+)?failed\s*[:.-]?\s*/ig, '')
                .replace(/MOVEit\s+COM(?:\s+API)?\s+(?:request\s+)?failed\s*[:.-]?\s*/ig, '')
                .replace(/MOVEit\s+COM\s+error(?:\s+0x[0-9A-F]+)?\s*[:.-]?\s*/ig, '')
                .replace(/^REST(?:\s+API)?\s+(?:request\s+)?failed\s*[:.-]?\s*/i, '')
                .replace(/^COM(?:\s+API)?\s+(?:request\s+)?failed\s*[:.-]?\s*/i, '')
                .replace(/\s{2,}/g, ' ')
                .trim();
            return text || 'The operation could not be completed.';
        }

        function setFooterStatus(message, kind = 'default') {
            if (!dirtyState) return;
            dirtyState.textContent = kind === 'error' ? friendlyEditorError(message) : (message || '');
            dirtyState.classList.toggle('error', kind === 'error');
        }

        async function overrideCheckout() {
            if (!(checkoutConflict || checkout?.lost || saveOutcomeUncertain) || saving || syncActionBusy || runAsOfBusy || !checkout?.override) return false;
            const signature = editorStateSignature();
            saving = true; content.inert = true; updateDirtyState();
            try {
                const revision = await checkout.override(activeTaskId);
                if (!revision) return false;
                if (signature !== editorStateSignature()) throw new Error('Editor values changed during recovery; try again.');
                editVersion = revision;
                saveOutcomeUncertain = false; checkoutConflict = false; manualRecoverySave = true;
                serverUndoCaptured = false; lastUndo = null; updateUndoState();
                footerSaveError = ''; autoSaveBlockedSignature = '';
                return true;
            } catch (recoveryError) { footerSaveError = recoveryError.message; return false; }
            finally { saving = false; content.inert = false; updateDirtyState(); }
        }

        function updatePrimarySaveButton() {
            if (saveButton) {
                saveButton.textContent = saving ? 'Saving…' : (autoSave && !manualRecoverySave && !saveOutcomeUncertain && !checkout?.lost ? 'Close' : 'Save');
                saveButton.title = autoSave && !manualRecoverySave && !saveOutcomeUncertain && !checkout?.lost ? 'Close the Task Editor after pending changes are saved.' : 'Save this Task to MOVEit Automation.';
            }
            if (cancelButton) cancelButton.hidden = autoSave && !manualRecoverySave && !saveOutcomeUncertain && !checkout?.lost;
        }

        function scheduleAutoSave() {
            window.clearTimeout(autoSaveTimer);
            autoSaveTimer = 0;
            if (manualRecoverySave || checkout?.lost || runAsOfBusy || editingHost || !autoSave || !dirty || saveOutcomeUncertain || saving || activeSavePromise || !canAlterTask || !isSupportedHostSavePattern()) return;
            if (autoSaveBlockedSignature && autoSaveBlockedSignature === editorStateSignature()) return;
            autoSaveTimer = window.setTimeout(() => {
                autoSaveTimer = 0;
                if (runAsOfBusy || editingHost) return;
                const focused = document.activeElement;
                const editingText = focused && dialog.contains(focused) && (
                    focused instanceof HTMLTextAreaElement
                    || (focused instanceof HTMLInputElement && !['checkbox', 'radio', 'button'].includes(focused.type))
                    || focused.isContentEditable);
                if (editingText) { scheduleAutoSave(); return; }
                saveEditor({ reload: false, automatic: true });
            }, 650);
        }

        async function saveTaskFlowImage() {
            closeActionsMenu();
            if (!isAdvancedTask() || !advancedEditor || content.hidden || saving || exportingFlow) return;
            const owner = activeTaskId;
            const name = String(dialog.querySelector('[data-editor-field="name"]')?.value || activeTaskName || 'Task');
            exportingFlow = true; updateDeleteAvailability();
            try {
                const blob = await advancedEditor.exportImage({ title: name, taskId: owner });
                const url = URL.createObjectURL(blob), link = document.createElement('a');
                const filename = name.normalize('NFKC').replace(/[<>:"/\\|?*\u0000-\u001f\u007f]/g, '_').trim().replace(/[ .]+$/g, '').slice(0, 100) || 'Task';
                link.href = url; link.download = `${filename}-task-flow.png`;
                document.body.append(link);
                try { link.click(); } finally { link.remove(); window.setTimeout(() => URL.revokeObjectURL(url), 60000); }
            } catch (error) {
                if (dialog.open && activeTaskId === owner) setFooterStatus(error?.message || 'Unable to save the Task flow image.', 'error');
            } finally { exportingFlow = false; updateDeleteAvailability(); }
        }

        function updateDeleteAvailability() {

            if (saveFlowAction) { saveFlowAction.hidden = !isAdvancedTask(); saveFlowAction.disabled = content.hidden || saving || exportingFlow; }

            if (cloneAction) cloneAction.disabled = !canCreateTask || content.hidden || !onCloneTask || !canAlterTask || saving || syncActionBusy || saveOutcomeUncertain;
            if (createAdvancedAction) {
                createAdvancedAction.hidden = activeTaskType.toLowerCase() !== 'traditional';
                createAdvancedAction.disabled = !canCreateTask || content.hidden || !canAlterTask || saving || syncActionBusy || saveOutcomeUncertain
                    || conversionOutcomeUncertain || config?.dataset.advancedTasksLicensed === 'false';
                createAdvancedAction.title = config?.dataset.advancedTasksLicensed === 'false' ? 'Advanced Tasks are not licensed.' : '';
            }
            if (deleteAction) deleteAction.disabled = !canDeleteTask || content.hidden || !onDeleteTask || !canAlterTask || saving || syncActionBusy || saveOutcomeUncertain;
        }

        const runAsOfModal = window.MauiTaskRunAsOf?.create({
            runUrl: () => endpoint('runAsOfUrl'), progressUrl: () => endpoint('runAsOfProgressUrl'), fetchJson,
            flush: flushPendingSave, context: () => ({ id:activeTaskId, editVersion }), signature:editorStateSignature,
            reload:reloadAuthoritativeEditor,
            busy: value => {
                runAsOfBusy = value; dialog.inert = value;
                if (value) { window.clearTimeout(autoSaveTimer); autoSaveTimer = 0; }
                updateDirtyState();
            },
            recovery: () => { saveOutcomeUncertain = true; footerSaveError = 'Inspect the Run As Of result and reload the Task before saving again.'; updateDirtyState(); }
        });
        runAsOfAction?.addEventListener('click', () => { closeActionsMenu(); if (!runAsOfAction.disabled) runAsOfModal?.open(); });

        function updateDirtyState() {
            if (!canAlterTask) autoSave=false;
            window.MauiModalUI.viewOnly(dialog,!canAlterTask);
            for (const button of [nextActionSaveTemplate, destinationSmtpSaveTemplate]) {
                if (button) button.disabled = !canSaveTemplates || !canAlterTask || saving || syncActionBusy || saveOutcomeUncertain;
            }
            const folderControl = dialog.querySelector('[data-editor-field="folderId"]');
            if (folderControl) folderControl.disabled = !canAlterTask || document.documentElement.dataset.moveitAdmin !== 'true' || saving;
            // Footer controls live outside the content region and need the same save lock.
            const enabledControl = dialog.querySelector('[data-editor-field="enabled"]');
            if (enabledControl) enabledControl.disabled = !canAlterTask || saving || syncActionBusy || runAsOfBusy;
            if (autoSaveToggle) autoSaveToggle.disabled = saving || syncActionBusy || runAsOfBusy;
            if (actionsButton) actionsButton.disabled = saving;
            updateEditHostButtons();
            if (runAsOfAction) {
                runAsOfAction.hidden = isSyncTask();
                runAsOfAction.disabled = !runAsOfModal || content.hidden || !canAlterTask || !currentCanRun || saving || runAsOfBusy || syncActionBusy || saveOutcomeUncertain
                    || !window.MauiTaskRunAsOf.available(buildMoveItStepPatches());
            }
            updateDeleteAvailability();
            dialog.querySelectorAll('[data-sync-action]').forEach(button => { button.disabled = saving || syncActionBusy || saveOutcomeUncertain || !canAlterTask || button.dataset.syncAction === 'swap' && taskSteps.length !== 2; });
            const previewAction = dialog.querySelector('[data-sync-view-preview]');
            if (previewAction) previewAction.disabled = saving || syncActionBusy || saveOutcomeUncertain || taskSteps.length !== 2;
            if (reloadButton) { reloadButton.hidden = !saveOutcomeUncertain && !checkout?.lost; reloadButton.disabled = saving || syncActionBusy; }
            if (overrideButton) { overrideButton.hidden = !(checkoutConflict || checkout?.lost || saveOutcomeUncertain) || document.documentElement.dataset.moveitAdmin !== 'true'; overrideButton.disabled = saving || syncActionBusy || runAsOfBusy; }
            if (isAdvancedTask()) advancedEditor?.refresh();
            const taskFieldsDirty = editorFields().some(control => baseline.has(control.dataset.editorField)
                && baseline.get(control.dataset.editorField) !== readControl(control));
            const stepsDirty = stepsBaseline !== stepsComparable();
            const schedulesDirty = schedulesBaseline !== schedulesComparable();
            const nextActionsDirty = nextActionsBaseline !== nextActionsComparable();
            const taskParametersDirty = taskParametersBaseline !== taskParametersComparable();
            dirty = canAlterTask && (taskFieldsDirty || stepsDirty || schedulesDirty || nextActionsDirty || taskParametersDirty || isAdvancedTask() && Boolean(advancedEditor?.dirty()));
            dialog.classList.toggle('has-unsaved-changes', dirty);
            const hostSaveSupported = isSupportedHostSavePattern();
            updatePrimarySaveButton();
            if (saveButton) saveButton.disabled = checkout?.lost || saveOutcomeUncertain || saving || editingHost || !canAlterTask || ((!autoSave || manualRecoverySave) && (!dirty || !hostSaveSupported));
            if (saving) setFooterStatus('Saving changes to MOVEit Automation…');
            else if (!canAlterTask) setFooterStatus(window.MauiModalUI.readOnlyMessage('Task'));
            else if (saveOutcomeUncertain) setFooterStatus(footerSaveError || 'Saving is blocked. Reload Task to discard unsaved changes and load the current MOVEit copy.', 'error');
            else if (footerSaveError) setFooterStatus(footerSaveError, 'error');
            else if (incompleteHostStep()) setFooterStatus(`Select a Host for the ${incompleteHostStep().kind} before saving.`);
            else if (!hostSaveSupported) setFooterStatus('One or more unsupported Source/Destination Steps were changed. Restore them, change them to a supported Host, or remove them before saving.');
            else if (manualRecoverySave) setFooterStatus(window.MauiObjectEditLease.restoredMessage);
            else if (dirty) setFooterStatus(autoSave ? 'Changes pending Auto Save…' : 'Unsaved changes.');
            else setFooterStatus('No unsaved changes.');
            scheduleAutoSave();
        }

        function hostStepConfiguration(model) {
            if (!model) return model;
            const { hostName, hostType, hostDefaults, ...configuration } = model;
            return configuration;
        }

        function stepsComparable() {
            return JSON.stringify(taskSteps.map(step => ({
                key: step.key,
                index: step.index,
                kind: step.kind,
                id: step.id,
                source: hostStepConfiguration(step.source),
                process: step.process,
                destination: hostStepConfiguration(step.destination)
            })));
        }


        function schedulesComparable() {
            return JSON.stringify(schedules.map(schedule => ({
                key: schedule.key,
                index: schedule.index,
                serverIndex: schedule.serverIndex,
                id: schedule.id,
                multi: schedule.multi,
                daysOfWeek: schedule.daysOfWeek,
                daysOfMonth: schedule.daysOfMonth,
                dateLists: schedule.dateLists,
                intervals: schedule.intervals,
                onlyUntilFirstSuccess: schedule.onlyUntilFirstSuccess,
                failIfNoSuccessInSchedule: schedule.failIfNoSuccessInSchedule,
                runEvenIfNotifications: schedule.runEvenIfNotifications
            })));
        }

        function taskParametersComparable() {
            return JSON.stringify(taskParameters.map(parameter => ({
                name: parameter.name, value: parameter.value, secure: parameter.secure, valueChanged: parameter.valueChanged
            })));
        }

        function nextActionsComparable() {
            return JSON.stringify(nextActions.map(action => ({
                key: action.key, index: action.index, serverIndex: action.serverIndex, id: action.id, type: action.type, doAfter: action.doAfter,
                doIfFailure: action.doIfFailure, doIfNoAction: action.doIfNoAction, doIfSuccess: action.doIfSuccess,
                isHtml: action.isHtml, hostId: action.hostId, addressTo: action.addressTo, subject: action.subject, message: action.message,
                taskId: action.taskId, wait: Boolean(action.wait), parameters: action.parameters
            })));
        }

        function refreshLinkedPresentation(control) {
            if (control.dataset.editorField?.startsWith('sync')) refreshSyncSettingsPresentation();
            if (control.matches('[data-traditional-task-editor-name]')) {
                activeTaskName = String(control.value || activeTaskId).trim();
                title.textContent = activeTaskName || activeTaskId || 'Task';
            }
            if (control.matches('[data-traditional-task-editor-description-field]')) {
                description.textContent = String(control.value || '').trim() || 'No description';
            }
            if (control.matches('[data-traditional-task-editor-use-default-state-cache]')) refreshAdvancedDependencies();
            if (control.matches('[data-traditional-task-editor-use-global-task-log-retention]')) refreshAdvancedDependencies();
            if (control.matches('[data-traditional-task-editor-state-cache-time]')) {
                const cacheTime = Number(control.value || 0);
                const mode = cacheTime < 0 ? 'always' : (cacheTime === 0 ? 'task' : 'for');
                stateCacheModeControls.forEach(item => { item.checked = item.value === mode; });
                if (stateCacheForValue) stateCacheForValue.value = cacheTime > 0 ? String(cacheTime) : '0';
                refreshAdvancedDependencies();
            }
        }

        function setTab(tabName, lazyLoad = true) {
            tabs.forEach(tab => {
                const selected = tab.dataset.traditionalTaskEditorTab === tabName;
                tab.classList.toggle('active', selected);
                tab.setAttribute('aria-selected', selected ? 'true' : 'false');
            });
            panels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorPanel !== tabName; });
            if (tabName === 'logs' && lazyLoad) loadLogs();
        }

        function setWorkspace(sectionName) {
            if (isAdvancedTask()) advancedEditor?.setWorkspace(sectionName);
            const railSection = isAdvancedTask() && ['advanced-step', 'next-actions'].includes(sectionName) ? 'steps' : sectionName;
            railSections.forEach(section => {
                const selected = section.dataset.traditionalTaskEditorSection === railSection;
                section.classList.toggle('active', selected);
                const items = section.querySelector('.traditional-task-editor-rail-items');
                const heading = section.querySelector('[data-traditional-task-editor-section-button]');
                if (items) items.hidden = !selected || isAdvancedTask() && section.dataset.traditionalTaskEditorSection === 'steps';
                if (heading) heading.setAttribute('aria-expanded', selected ? 'true' : 'false');
            });
            workspacePanels.forEach(panel => { panel.hidden = panel.dataset.traditionalTaskEditorWorkspace !== sectionName; });
            if (sectionName === 'schedules') {
                if (activeScheduleKey && schedules.some(schedule => schedule.key === activeScheduleKey)) selectSchedule(activeScheduleKey);
                else if (schedules.length) selectSchedule(schedules[0].key);
                else showNoSchedule();
            }
            if (sectionName === 'next-actions') {
                if (activeNextActionKey && nextActions.some(action => action.key === activeNextActionKey)) selectNextAction(activeNextActionKey);
                else if (nextActions.length) selectNextAction(nextActions[0].key);
                else showNoNextAction();
            }
        }

        async function loadLogs() {
            if (!activeTaskId) return;
            logsRequest?.abort();
            const request = new AbortController(), owner = ++logsGeneration, taskId = activeTaskId;
            logsRequest = request;
            if (logsLoading) logsLoading.hidden = false;
            if (logsError) { logsError.hidden = true; logsError.textContent = ''; }
            if (logsEmpty) logsEmpty.hidden = true;
            if (logsList) logsList.hidden = true;
            logsRows?.replaceChildren();

            try {
                const baseUrl = endpoint('taskLogsUrl');
                if (!baseUrl) throw new Error('The Task log endpoint is not configured.');
                const url = new URL(baseUrl, window.location.href);
                url.searchParams.set('id', taskId);
                const data = await fetchJson(url.toString(), { signal: request.signal, cache: 'no-store' });
                if (owner !== logsGeneration || taskId !== activeTaskId || !dialog.open) return;
                renderLogs(data?.logs || []);
            } catch (requestError) {
                if (owner !== logsGeneration || request.signal.aborted || taskId !== activeTaskId || !dialog.open) return;
                if (logsError) { logsError.textContent = requestError?.message || 'Unable to load Task Logs.'; logsError.hidden = false; }
            } finally {
                if (owner === logsGeneration) { logsRequest = null; if (logsLoading) logsLoading.hidden = true; }
            }
        }

        function renderLogs(logs) {
            logsRows?.replaceChildren();
            if (!Array.isArray(logs) || logs.length === 0) {
                if (logsEmpty) logsEmpty.hidden = false;
                if (logsList) logsList.hidden = true;
                return;
            }

            const fragment = document.createDocumentFragment();
            logs.forEach(log => {
                const row = document.createElement('div');
                row.className = 'task-log-row';
                row.setAttribute('role', 'row');

                const resultCell = document.createElement('div');
                resultCell.className = 'task-log-result-cell';
                resultCell.setAttribute('role', 'cell');
                const indicator = document.createElement('span');
                const status = classifyTaskLogStatus(log.exitStatus);
                indicator.className = `task-log-status ${status.kind}`;
                indicator.textContent = status.symbol;
                indicator.title = log.exitStatus || status.label;
                indicator.setAttribute('aria-label', log.exitStatus || status.label);
                resultCell.appendChild(indicator);

                const logCell = document.createElement('div');
                logCell.className = 'task-log-name-cell';
                logCell.setAttribute('role', 'cell');
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'task-log-link';
                button.dataset.traditionalTaskEditorLogId = String(log.id ?? '');
                button.textContent = formatTaskLogName(log);
                button.title = 'Open task log';
                button.disabled = log.id == null || String(log.id).trim() === '';
                logCell.appendChild(button);

                row.append(resultCell, logCell);
                fragment.appendChild(row);
            });
            logsRows?.appendChild(fragment);
            if (logsEmpty) logsEmpty.hidden = true;
            if (logsList) logsList.hidden = false;
        }

        function classifyTaskLogStatus(exitStatus) {
            const value = String(exitStatus || '').trim().toLowerCase();
            if (value.includes('success')) return { kind: 'success', symbol: '✓', label: 'Success' };
            if (value.includes('fail') || value.includes('error')) return { kind: 'failure', symbol: '!', label: 'Failure' };
            if (value.includes('no xfer') || value.includes('noxfer') || value.includes('no transfer')) return { kind: 'neutral', symbol: '−', label: 'No Transfers' };
            return { kind: 'neutral', symbol: '•', label: exitStatus || 'Unknown' };
        }

        function formatTaskLogName(log) {
            if (log?.startTime) {
                const date = new Date(log.startTime);
                if (!Number.isNaN(date.valueOf())) return date.toLocaleString();
                return String(log.startTime);
            }
            const fallbackName = String(log?.fallbackName || log?.logPath || '').trim();
            if (fallbackName) return fallbackName;
            return `Task Log ${log?.id || ''}`.trim();
        }

        function openTaskLogViewer(logId) {
            const baseUrl = endpoint('taskLogViewerUrl');
            const normalizedLogId = String(logId || '').trim();
            if (!baseUrl || !activeTaskId || !normalizedLogId) return;
            const url = new URL(baseUrl, window.location.href);
            url.searchParams.set('id', activeTaskId);
            url.searchParams.set('logId', normalizedLogId);
            const currentWidth = window.outerWidth || window.screen.availWidth || 1200;
            const currentHeight = window.outerHeight || window.screen.availHeight || 800;
            const width = Math.max(760, currentWidth - 120);
            const height = Math.max(560, currentHeight - 120);
            const currentLeft = typeof window.screenX === 'number' ? window.screenX : (window.screenLeft || 0);
            const currentTop = typeof window.screenY === 'number' ? window.screenY : (window.screenTop || 0);
            const left = Math.max(0, Math.round(currentLeft + ((currentWidth - width) / 2)));
            const top = Math.max(0, Math.round(currentTop + ((currentHeight - height) / 2)));
            const popup = window.open(url.toString(), `MauiTaskLog_${activeTaskId}_${normalizedLogId}`, [
                'popup=yes', 'resizable=yes', 'scrollbars=yes', `width=${Math.round(width)}`, `height=${Math.round(height)}`, `left=${left}`, `top=${top}`
            ].join(','));
            if (!popup && logsError) {
                logsError.textContent = 'The Task Log viewer popup was blocked by the browser.';
                logsError.hidden = false;
            }
        }

        async function closeDialogWithCheckout() {
            dialog.inert = true;
            try { await checkout?.close(); dialog.close(); }
            finally { dialog.inert = false; }
        }

        async function requestClose() {
            if (saving || syncActionBusy || runAsOfBusy || editingHost) return false;
            closeActionsMenu();
            closeAddStepMenu();
            closeAddNextActionMenu();
            closeStepActionsMenu();
            closeScheduleActionsMenu();
            closeNextActionActionsMenu();
            if (!canAlterTask) { await closeDialogWithCheckout(); return true; }
            if (autoSave && !manualRecoverySave && !saveOutcomeUncertain && !checkout?.lost) {
                const saved = await flushPendingSave();
                if (saved) await closeDialogWithCheckout();
                return saved;
            }
            if (!dirty) { await closeDialogWithCheckout(); return true; }
            const confirmed = await window.mauiConfirm?.({
                title: 'Discard Changes?',
                message: 'You have unsaved changes in the Task Editor. Closing the editor will discard them.',
                confirmText: 'Discard Changes',
                danger: true
            });
            if (!confirmed) return false;
            dirty = false;
            await closeDialogWithCheckout();
            return true;
        }

        // Reuse the page's guarded delete workflow, just as the Host editor does.
        async function deleteCurrentTask() {
            closeActionsMenu();
            if (content.hidden || !canDeleteTask || !onDeleteTask || !canAlterTask || saving || syncActionBusy || saveOutcomeUncertain) return false;
            const id = activeTaskId, name = activeTaskName;
            if (!await requestClose()) return false;
            await onDeleteTask(id, name);
            return true;
        }

        async function cloneCurrentTask() {
            closeActionsMenu();
            if (content.hidden || !canCreateTask || !onCloneTask || !canAlterTask || saving || syncActionBusy || saveOutcomeUncertain) return false;
            if (!await flushPendingSave()) return false;
            const id = activeTaskId, name = activeTaskName, type = activeTaskType;
            await closeDialogWithCheckout();
            await onCloneTask(id, name, type);
            return true;
        }

        async function createAdvancedCurrentTask() {
            closeActionsMenu();
            if (content.hidden || !canCreateTask || activeTaskType.toLowerCase() !== 'traditional' || !canAlterTask || saving
                || syncActionBusy || saveOutcomeUncertain || conversionOutcomeUncertain || createAdvancedAction?.disabled) return false;
            if (!await flushPendingSave()) return false;
            const id = activeTaskId;
            const url = endpoint('createAdvancedTaskUrl');
            if (!url) { footerSaveError = 'The Create Advanced Task endpoint is not configured.'; updateDirtyState(); return false; }
            window.clearTimeout(autoSaveTimer); autoSaveTimer = 0;
            saving = true; dialog.inert = true; footerSaveError = '';
            updateDirtyState(); setFooterStatus('Creating disabled Advanced Task…');
            let created = null;
            try {
                created = await fetchJson(url, {method:'POST',body:JSON.stringify({taskId:id})});
                if (!String(created?.taskId || '').trim()) {
                    const error = new Error('MAUI could not confirm the new Advanced Task ID. Inspect Tasks before creating another copy.');
                    error.payload = {outcomeUnknown:true}; throw error;
                }
            } catch (failure) {
                conversionOutcomeUncertain = Boolean(failure?.payload?.outcomeUnknown) || !failure?.status;
                footerSaveError = failure?.message || 'Unable to create the Advanced Task.';
                if (conversionOutcomeUncertain && !footerSaveError.includes('Inspect')) footerSaveError += ' Inspect Tasks before creating another copy.';
            } finally {
                saving = false; dialog.inert = false; updateDirtyState();
            }
            if (!created?.taskId) return false;
            const opened = await open(String(created.taskId), String(created.taskName || ''), 'Advanced');
            if (opened) setFooterStatus(created.warning || `Created disabled Advanced Task ${created.taskName || created.taskId}.`, created.warning ? 'error' : 'default');
            else if (error) error.textContent = `Advanced Task ${created.taskName || created.taskId} was created, but its editor could not be loaded. Reopen it from Tasks.`;
            return true;
        }

        async function saveEditorNotificationTemplate(context) {
            if (!canSaveTemplates || !onSaveNotificationTemplate || !canAlterTask || saving || syncActionBusy || saveOutcomeUncertain) return;
            const smtp = context === 'smtp';
            const target = smtp ? activeSmtpDestination() : activeNextAction();
            const key = smtp ? activeStep()?.key : target?.key;
            if (!target || !key || (!smtp && String(target.type).toLowerCase() !== 'email')) return;
            const message = smtp ? currentDestinationSmtpMessage() : currentNextActionMessage();
            const owner = activeTaskId, selection = `${context}:${key}`;
            const format = target.isHtml ? 'html' : 'text';
            const saved = await onSaveNotificationTemplate({ subject: target.subject || '', message, format,
                templateId: templateSelections.get(selection) || '', templates: notificationTemplates });
            if (!saved || owner !== activeTaskId) return;
            notificationTemplates = [...notificationTemplates.filter(item => item.id !== saved.id), saved];
            templateSelections.set(selection, saved.id);
            populateNextActionTemplates(); populateDestinationSmtpTemplates();
            setFooterStatus(`Saved notification template ${saved.name}.`);
        }

        function toggleActionsMenu() {
            if (!actionsMenu || !actionsButton) return;
            closeAddStepMenu();
            closeAddNextActionMenu();
            closeStepActionsMenu();
            closeScheduleActionsMenu();
            closeNextActionActionsMenu();
            const show = actionsMenu.hidden;
            actionsMenu.hidden = !show;
            actionsButton.setAttribute('aria-expanded', show ? 'true' : 'false');
        }

        function closeActionsMenu() {
            if (actionsMenu) actionsMenu.hidden = true;
            if (actionsButton) actionsButton.setAttribute('aria-expanded', 'false');
        }

        function toggleStepActionsMenu() {
            if (!stepActionsMenu || !stepActionsButton || !activeStep()) return;
            closeActionsMenu();
            closeAddStepMenu();
            closeAddNextActionMenu();
            closeScheduleActionsMenu();
            closeNextActionActionsMenu();
            const show = stepActionsMenu.hidden;
            stepActionsMenu.hidden = !show;
            stepActionsButton.setAttribute('aria-expanded', show ? 'true' : 'false');
        }

        function closeStepActionsMenu() {
            if (stepActionsMenu) stepActionsMenu.hidden = true;
            if (stepActionsButton) stepActionsButton.setAttribute('aria-expanded', 'false');
        }

        function handleStepAction(action) {
            const step = activeStep();
            if (!step) return;
            closeStepActionsMenu();
            if (action === 'delete') {
                removeActiveStep();
                return;
            }
            if (action === 'clone') {
                cloneActiveStep();
                return;
            }
            if (action === 'source-timestamps') {
                openSourceTimestamps();
            }
        }

        function clearNextActionParameterError() {
            if (!nextActionParameterError) return;
            nextActionParameterError.hidden = true;
            nextActionParameterError.textContent = '';
        }

        function showNextActionParameterError(message, control) {
            if (nextActionParameterError) {
                nextActionParameterError.textContent = friendlyEditorError(message || 'Unable to update the Run Task parameter.');
                nextActionParameterError.hidden = false;
            }
            control?.focus?.();
        }

        function openNextActionParameterEditor(parameterName = '') {
            const action = activeNextAction();
            if (!action || String(action.type || '').toLowerCase() !== 'task' || !nextActionParameterDialog) return;
            const normalizedName = String(parameterName || '').trim();
            const existing = action.parameters.find(item => item.name.toLowerCase() === normalizedName.toLowerCase()) || null;
            const secureExisting = Boolean(existing?.secure);
            nextActionParameterEditName = existing?.name || '';
            clearNextActionParameterError();
            if (nextActionParameterTitle) nextActionParameterTitle.textContent = secureExisting
                ? 'Change Secure Run Task Parameter'
                : (existing ? 'Edit Run Task Parameter' : 'Add Run Task Parameter');
            if (nextActionParameterSubtitle) nextActionParameterSubtitle.textContent = secureExisting
                ? 'The existing secure value cannot be displayed. Enter a new value to replace it.'
                : 'Define a parameter passed to the Task to Run.';
            if (nextActionParameterName) {
                nextActionParameterName.value = existing?.name || '';
                nextActionParameterName.disabled = secureExisting;
            }
            if (nextActionParameterValue) {
                nextActionParameterValue.value = secureExisting ? '' : (existing?.value || '');
                nextActionParameterValue.disabled = false;
                nextActionParameterValue.placeholder = secureExisting ? 'Enter new secure value' : 'Value';
            }
            if (nextActionParameterSecure) {
                nextActionParameterSecure.checked = Boolean(existing?.secure);
                nextActionParameterSecure.disabled = secureExisting;
            }
            if (nextActionParameterSave) nextActionParameterSave.textContent = secureExisting ? 'Change Value' : 'Apply';
            nextActionParameterDialog.showModal();
            (secureExisting ? nextActionParameterValue : nextActionParameterName)?.focus();
        }

        function applyNextActionParameterEditor() {
            const action = activeNextAction();
            if (!action || String(action.type || '').toLowerCase() !== 'task') return;
            const existing = action.parameters.find(item => item.name.toLowerCase() === nextActionParameterEditName.toLowerCase()) || null;
            const secureExisting = Boolean(existing?.secure);
            const name = secureExisting ? String(existing?.name || '').trim() : String(nextActionParameterName?.value || '').trim();
            const value = String(nextActionParameterValue?.value || '');
            const secure = secureExisting ? true : Boolean(nextActionParameterSecure?.checked);
            const nameError = validateParameterName(name, 'Parameter');
            if (nameError) { showNextActionParameterError(nameError, nextActionParameterName); return; }
            if (secureExisting && value.length === 0) {
                showNextActionParameterError(`Enter a new value for secure Run Task Parameter ${name}.`, nextActionParameterValue);
                return;
            }
            const duplicate = action.parameters.find(item => item !== existing && item.name.toLowerCase() === name.toLowerCase());
            if (duplicate) { showNextActionParameterError(`A Run Task parameter named "${name}" already exists.`, nextActionParameterName); return; }
            const before = deepClone(action);
            const replacement = {
                name,
                originalName: existing?.originalName || existing?.name || name,
                value,
                secure,
                valueChanged: secureExisting ? true : (secure ? true : Boolean(existing?.valueChanged))
            };
            if (existing) action.parameters.splice(action.parameters.indexOf(existing), 1, replacement);
            else action.parameters.push(replacement);
            action.parameters.sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }));
            lastUndo = { kind: 'next-action-snapshot', actionKey: action.key, before, label: secureExisting ? `Change Run Task Parameter ${name}` : (existing ? `Edit Run Task Parameter ${name}` : `Add Run Task Parameter ${name}`) };
            nextActionParameterEditName = '';
            nextActionParameterDialog.close();
            seedNextActionStableValues(action);
            renderNextActionParameters(action);
            renderNextActionRail();
            updateUndoState();
            footerSaveError = '';
            updateDirtyState();
        }

        function removeNextActionParameter(parameterName) {
            const action = activeNextAction();
            if (!action || String(action.type || '').toLowerCase() !== 'task') return;
            const index = action.parameters.findIndex(item => item.name.toLowerCase() === String(parameterName || '').trim().toLowerCase());
            if (index < 0) return;
            const before = deepClone(action);
            const removed = action.parameters[index];
            action.parameters.splice(index, 1);
            lastUndo = { kind: 'next-action-snapshot', actionKey: action.key, before, label: `Remove Run Task Parameter ${removed.name}` };
            seedNextActionStableValues(action);
            renderNextActionParameters(action);
            renderNextActionRail();
            updateUndoState();
            footerSaveError = '';
            updateDirtyState();
        }

        function openTaskParameters() {
            closeActionsMenu();
            renderTaskParameters();
            if (parametersDialog && !parametersDialog.open) parametersDialog.showModal();
        }

        function openTaskParameterEditor(parameterName = '') {
            if (!addParameterDialog) return;
            const normalizedName = String(parameterName || '').trim();
            const existing = taskParameters.find(item => item.name.toLowerCase() === normalizedName.toLowerCase()) || null;
            const secureExisting = Boolean(existing?.secure);
            taskParameterEditName = existing?.name || '';
            const definition = scriptGuidance?.definition(normalizedName, 'task');
            scriptGuidance?.parameterHelp(addParameterDialog.querySelector('[data-task-script-parameter-help]'), definition);
            if (taskParameterError) { taskParameterError.hidden = true; taskParameterError.textContent = ''; }

            if (taskParameterDialogTitle) {
                taskParameterDialogTitle.textContent = secureExisting
                    ? 'Change Secure Task Parameter'
                    : (existing ? 'Edit Task Parameter' : 'Add Parameter');
            }
            if (taskParameterDialogSubtitle) {
                taskParameterDialogSubtitle.textContent = secureExisting
                    ? 'The existing secure value cannot be displayed. Enter a new value to replace it.'
                    : 'Define a task-scoped parameter.';
            }
            if (taskParameterName) {
                taskParameterName.value = existing?.name || normalizedName;
                taskParameterName.disabled = secureExisting;
                taskParameterName.readOnly = !existing && Boolean(definition);
            }
            if (taskParameterValue) {
                taskParameterValue.value = secureExisting ? '' : (existing?.value ?? definition?.defaultValue ?? '');
                taskParameterValue.disabled = false;
                taskParameterValue.placeholder = secureExisting ? 'Enter new secure value' : 'Value';
            }
            if (taskParameterSecure) {
                taskParameterSecure.checked = Boolean(existing?.secure);
                taskParameterSecure.disabled = secureExisting;
            }
            if (taskParameterSave) taskParameterSave.textContent = secureExisting ? 'Change Value' : 'Apply';
            addParameterDialog.showModal();
            (secureExisting ? taskParameterValue : taskParameterName)?.focus();
        }

        function validateParameterName(name, label) {
            if (!name) return `${label} Name is required.`;
            if (!isValidParameterName(name)) return `${label} Name must start with a letter or underscore and contain only letters, numbers, dots, underscores, or colons.`;
            return '';
        }

        function showTaskParameterError(message, control) {
            if (taskParameterError) {
                taskParameterError.textContent = friendlyEditorError(message);
                taskParameterError.hidden = false;
            }
            control?.focus?.();
        }

        function applyTaskParameterEditor() {
            const existing = taskParameters.find(item => item.name.toLowerCase() === taskParameterEditName.toLowerCase()) || null;
            const secureExisting = Boolean(existing?.secure);
            const name = secureExisting ? String(existing?.name || '').trim() : String(taskParameterName?.value || '').trim();
            const value = String(taskParameterValue?.value || '');
            const secure = secureExisting ? true : Boolean(taskParameterSecure?.checked);
            const nameError = validateParameterName(name, 'Parameter');
            if (nameError) { showTaskParameterError(nameError, taskParameterName); return; }
            if (secureExisting && value.length === 0) {
                showTaskParameterError(`Enter a new value for secure Task Parameter ${name}.`, taskParameterValue);
                return;
            }
            const duplicate = taskParameters.find(item => item !== existing && item.name.toLowerCase() === name.toLowerCase());
            if (duplicate) { showTaskParameterError(`A Task parameter named "${name}" already exists.`, taskParameterName); return; }
            const definition = scriptGuidance?.definition(name, 'task');
            if (definition?.required && !value.length && !definition.defaultValue) {
                showTaskParameterError(`Enter a value for required parameter ${name}.`, taskParameterValue); return;
            }
            const before = deepClone(taskParameters);
            const replacement = {
                name,
                originalName: existing?.originalName || existing?.name || name,
                value,
                secure,
                valueChanged: secureExisting ? true : (secure ? true : Boolean(existing?.valueChanged))
            };
            if (existing) taskParameters.splice(taskParameters.indexOf(existing), 1, replacement);
            else taskParameters.push(replacement);
            taskParameters.sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }));
            lastUndo = { kind: 'task-parameters-snapshot', before, label: secureExisting ? `Change Task Parameter ${name}` : (existing ? `Edit Task Parameter ${name}` : `Add Task Parameter ${name}`) };
            taskParameterEditName = '';
            addParameterDialog.close();
            renderTaskParameters();
            scriptGuidance?.show(activeStep()?.process);
            updateUndoState();
            footerSaveError = '';
            updateDirtyState();
        }

        function removeTaskParameter(parameterName) {
            const index = taskParameters.findIndex(item => item.name.toLowerCase() === String(parameterName || '').trim().toLowerCase());
            if (index < 0) return;
            const before = deepClone(taskParameters);
            const removed = taskParameters[index];
            taskParameters.splice(index, 1);
            lastUndo = { kind: 'task-parameters-snapshot', before, label: `Remove Task Parameter ${removed.name}` };
            renderTaskParameters();
            scriptGuidance?.show(activeStep()?.process);
            updateUndoState();
            footerSaveError = '';
            updateDirtyState();
        }

        function openAddParameter() { openTaskParameterEditor(''); }

        function formatTransferExceptionSize(bytes) {
            const value = Number(bytes || 0);
            if (!Number.isFinite(value) || value <= 0) return '0 B';
            const units = ['B', 'KB', 'MB', 'GB', 'TB'];
            let size = value;
            let unit = 0;
            while (size >= 1024 && unit < units.length - 1) { size /= 1024; unit += 1; }
            const digits = unit === 0 || size >= 100 ? 0 : 1;
            return `${size.toFixed(digits)} ${units[unit]}`;
        }

        function transferExceptionSourceLabel(sourceId) {
            const sources = taskSteps.filter(step => String(step?.kind || '').toLowerCase() === 'source');
            const index = sources.findIndex(step => String(step?.source?.id || step?.id || '') === String(sourceId || ''));
            return index >= 0 ? `[ ${index + 1} ]` : '[ ? ]';
        }

        function renderTransferExceptions() {
            if (!transferExceptionsRows) return;
            transferExceptionsRows.replaceChildren();
            for (const entry of transferExceptions) {
                const row = document.createElement('div');
                row.className = 'traditional-task-editor-exception-row';
                row.innerHTML = '<div></div><div></div><div></div><div></div><div></div>';

                row.children[0].textContent = transferExceptionSourceLabel(entry.sourceId);
                row.children[0].title = entry.sourceId ? `MOVEit Source ID ${entry.sourceId}` : '';
                row.children[1].textContent = String(entry.fullPath || '');
                row.children[1].title = String(entry.fullPath || '');
                row.children[2].textContent = formatTransferExceptionSize(entry.sizeBytes);
                row.children[3].textContent = String(entry.timestamp || '');

                const remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'secondary-button compact-button danger-text-button';
                remove.textContent = 'Remove';
                remove.dataset.transferExceptionRemove = String(entry.exceptionId || '');
                remove.disabled = transferExceptionsBusy || !String(entry.exceptionId || '').trim();
                row.children[4].appendChild(remove);
                transferExceptionsRows.appendChild(row);
            }
            if (transferExceptionsEmpty) transferExceptionsEmpty.hidden = transferExceptions.length > 0;
        }

        async function loadTransferExceptions() {
            const baseUrl = endpoint('traditionalTaskEditorTransferExceptionsUrl');
            if (!baseUrl || !activeTaskId || transferExceptionsBusy) return;
            transferExceptionsBusy = true;
            if (transferExceptionsLoading) transferExceptionsLoading.hidden = false;
            if (transferExceptionsError) { transferExceptionsError.hidden = true; transferExceptionsError.textContent = ''; }
            if (transferExceptionsStatus) transferExceptionsStatus.textContent = '';
            renderTransferExceptions();
            try {
                const url = new URL(baseUrl, window.location.href);
                url.searchParams.set('id', activeTaskId);
                const payload = await fetchJson(url.toString());
                transferExceptions = Array.isArray(payload?.entries) ? payload.entries : [];
            } catch (requestError) {
                transferExceptions = [];
                if (transferExceptionsError) {
                    transferExceptionsError.textContent = requestError?.message || 'Unable to load transfer exceptions.';
                    transferExceptionsError.hidden = false;
                }
            } finally {
                transferExceptionsBusy = false;
                if (transferExceptionsLoading) transferExceptionsLoading.hidden = true;
                renderTransferExceptions();
            }
        }

        async function removeTransferException(exceptionId) {
            const id = String(exceptionId || '').trim();
            const baseUrl = endpoint('traditionalTaskEditorDeleteTransferExceptionsUrl');
            if (!id || !baseUrl || !activeTaskId || transferExceptionsBusy) return;
            transferExceptionsBusy = true;
            if (transferExceptionsError) { transferExceptionsError.hidden = true; transferExceptionsError.textContent = ''; }
            if (transferExceptionsStatus) transferExceptionsStatus.textContent = 'Removing transfer exception…';
            renderTransferExceptions();
            try {
                const url = new URL(baseUrl, window.location.href);
                url.searchParams.set('id', activeTaskId);
                await fetchJson(url.toString(), { method: 'POST', body: JSON.stringify([id]) });
                transferExceptionsBusy = false;
                if (transferExceptionsStatus) transferExceptionsStatus.textContent = 'Transfer exception removed. Refreshing…';
                await loadTransferExceptions();
            } catch (requestError) {
                if (transferExceptionsError) {
                    transferExceptionsError.textContent = requestError?.message || 'Unable to remove the transfer exception.';
                    transferExceptionsError.hidden = false;
                }
                if (transferExceptionsStatus) transferExceptionsStatus.textContent = '';
            } finally {
                transferExceptionsBusy = false;
                renderTransferExceptions();
            }
        }

        async function openTransferExceptions() {
            closeActionsMenu();
            if (!transferExceptionsDialog) return;
            const taskNameHost = transferExceptionsDialog.querySelector('[data-traditional-task-editor-transfer-exceptions-task-name]');
            if (taskNameHost) taskNameHost.textContent = activeTaskName || 'Traditional Task';
            transferExceptions = [];
            renderTransferExceptions();
            if (!transferExceptionsDialog.open) transferExceptionsDialog.showModal();
            await loadTransferExceptions();
        }

        function normalizeSourceTimestampEntry(item) {
            return {
                hostId: String(item?.hostId ?? '').trim(),
                path: String(item?.path ?? '').trim(),
                mask: String(item?.mask ?? '').trim(),
                timestamp: String(item?.timestamp ?? '').trim(),
                dst: String(item?.dst ?? '').trim(),
                version: Number(item?.version || 0),
                updatedStamp: String(item?.updatedStamp ?? '').trim()
            };
        }

        function isValidSourceTimestamp(value) {
            const match = /^(\d{4})-(\d{2})-(\d{2}) ([01]\d|2[0-3]):([0-5]\d):([0-5]\d)$/.exec(String(value || '').trim());
            if (!match) return false;
            const year = Number(match[1]);
            const month = Number(match[2]);
            const day = Number(match[3]);
            const hour = Number(match[4]);
            const minute = Number(match[5]);
            const second = Number(match[6]);
            const candidate = new Date(Date.UTC(year, month - 1, day, hour, minute, second));
            return candidate.getUTCFullYear() === year
                && candidate.getUTCMonth() === month - 1
                && candidate.getUTCDate() === day
                && candidate.getUTCHours() === hour
                && candidate.getUTCMinutes() === minute
                && candidate.getUTCSeconds() === second;
        }

        function setSourceTimestampEditError(message = '') {
            if (!sourceTimestampEditError) return;
            sourceTimestampEditError.textContent = message;
            sourceTimestampEditError.hidden = !message;
        }

        function renderSourceTimestamps() {
            if (!sourceTimestampsRows) return;
            sourceTimestampsRows.replaceChildren();
            sourceTimestamps.forEach((entry, index) => {
                const row = document.createElement('div');
                row.className = 'traditional-task-editor-source-timestamp-row';
                row.innerHTML = '<div></div><div></div><div></div><div></div>';
                row.children[0].textContent = entry.path;
                row.children[0].title = entry.path;
                row.children[1].textContent = entry.mask;
                row.children[1].title = entry.mask;
                row.children[2].textContent = entry.timestamp;

                const edit = document.createElement('button');
                edit.type = 'button';
                edit.className = 'traditional-task-editor-source-timestamp-edit';
                edit.dataset.traditionalTaskEditorSourceTimestampEdit = String(index);
                edit.title = 'Edit timestamp';
                edit.setAttribute('aria-label', `Edit timestamp for ${entry.path} ${entry.mask}`);
                edit.textContent = '✎';
                edit.disabled = sourceTimestampsBusy;
                row.children[3].appendChild(edit);
                sourceTimestampsRows.appendChild(row);
            });
            if (sourceTimestampsEmpty) sourceTimestampsEmpty.hidden = sourceTimestampsBusy || sourceTimestamps.length > 0;
            if (sourceTimestampAdd) sourceTimestampAdd.disabled = sourceTimestampsBusy || !sourceTimestampContext;
        }

        async function loadSourceTimestamps() {
            const baseUrl = endpoint('traditionalTaskEditorSourceTimestampsUrl');
            if (!baseUrl || !activeTaskId || !sourceTimestampContext?.hostId || sourceTimestampsBusy) return;
            sourceTimestampsBusy = true;
            if (sourceTimestampsLoading) sourceTimestampsLoading.hidden = false;
            if (sourceTimestampsError) { sourceTimestampsError.hidden = true; sourceTimestampsError.textContent = ''; }
            if (sourceTimestampsStatus) sourceTimestampsStatus.textContent = '';
            renderSourceTimestamps();
            try {
                const url = new URL(baseUrl, window.location.href);
                url.searchParams.set('id', activeTaskId);
                url.searchParams.set('hostId', sourceTimestampContext.hostId);
                const payload = await fetchJson(url.toString());
                sourceTimestamps = Array.isArray(payload?.entries) ? payload.entries.map(normalizeSourceTimestampEntry) : [];
                renderSourceTimestamps();
            } catch (requestError) {
                sourceTimestamps = [];
                renderSourceTimestamps();
                if (sourceTimestampsError) {
                    sourceTimestampsError.textContent = requestError?.message || 'Unable to load source timestamps.';
                    sourceTimestampsError.hidden = false;
                }
            } finally {
                sourceTimestampsBusy = false;
                if (sourceTimestampsLoading) sourceTimestampsLoading.hidden = true;
                renderSourceTimestamps();
                if (sourceTimestampsEmpty && sourceTimestampsError && !sourceTimestampsError.hidden) sourceTimestampsEmpty.hidden = true;
            }
        }

        function openSourceTimestampEditor(index = -1) {
            if (!sourceTimestampEditDialog || !sourceTimestampContext) return;
            const editing = Number.isInteger(index) && index >= 0 && index < sourceTimestamps.length;
            const entry = editing ? sourceTimestamps[index] : null;
            sourceTimestampEditIndex = editing ? index : -1;
            setSourceTimestampEditError('');

            if (sourceTimestampEditTitle) sourceTimestampEditTitle.textContent = editing ? 'Edit Source Timestamp' : 'Add Source Timestamp';
            if (sourceTimestampEditHost) sourceTimestampEditHost.textContent = sourceTimestampContext.hostName || 'Source Host';
            if (sourceTimestampPath) {
                sourceTimestampPath.value = editing ? entry.path : sourceTimestampContext.path;
                sourceTimestampPath.readOnly = editing;
            }
            if (sourceTimestampMask) {
                sourceTimestampMask.value = editing ? entry.mask : sourceTimestampContext.mask;
                sourceTimestampMask.readOnly = editing;
            }
            if (sourceTimestampValue) sourceTimestampValue.value = editing ? entry.timestamp : '';
            if (sourceTimestampHelp) sourceTimestampHelp.textContent = editing
                ? 'Use MOVEit server-local time in YYYY-MM-DD HH:mm:ss format. Leave blank to treat all matching files as new.'
                : 'Use MOVEit server-local time in YYYY-MM-DD HH:mm:ss format.';

            if (!sourceTimestampEditDialog.open) sourceTimestampEditDialog.showModal();
            window.setTimeout(() => sourceTimestampValue?.focus(), 0);
        }

        async function saveSourceTimestamp() {
            const baseUrl = endpoint('traditionalTaskEditorUpdateSourceTimestampUrl');
            if (!baseUrl || !activeTaskId || !sourceTimestampContext?.hostId || sourceTimestampsBusy) return;

            const path = String(sourceTimestampPath?.value || '').trim();
            const mask = String(sourceTimestampMask?.value || '').trim();
            const timestamp = String(sourceTimestampValue?.value || '').trim();
            const editing = sourceTimestampEditIndex >= 0;
            if (!path) { setSourceTimestampEditError('Path is required.'); sourceTimestampPath?.focus(); return; }
            if (!mask) { setSourceTimestampEditError('File mask is required.'); sourceTimestampMask?.focus(); return; }
            if (!timestamp && !editing) { setSourceTimestampEditError('Timestamp is required when adding a new source timestamp.'); sourceTimestampValue?.focus(); return; }
            if (timestamp && !isValidSourceTimestamp(timestamp)) {
                setSourceTimestampEditError('Timestamp must use YYYY-MM-DD HH:mm:ss and contain a valid date and time.');
                sourceTimestampValue?.focus();
                return;
            }

            sourceTimestampsBusy = true;
            setSourceTimestampEditError('');
            if (sourceTimestampSave) sourceTimestampSave.disabled = true;
            if (sourceTimestampsStatus) sourceTimestampsStatus.textContent = editing ? 'Saving timestamp…' : 'Adding timestamp…';
            try {
                const url = new URL(baseUrl, window.location.href);
                url.searchParams.set('id', activeTaskId);
                await fetchJson(url.toString(), {
                    method: 'PUT',
                    body: JSON.stringify({
                        hostId: sourceTimestampContext.hostId,
                        path,
                        mask,
                        timestamp
                    })
                });
                sourceTimestampEditDialog.close();
                sourceTimestampEditIndex = -1;
                sourceTimestampsBusy = false;
                if (sourceTimestampsStatus) sourceTimestampsStatus.textContent = 'Timestamp saved. Refreshing…';
                await loadSourceTimestamps();
            } catch (requestError) {
                if (sourceTimestampsStatus) sourceTimestampsStatus.textContent = '';
                setSourceTimestampEditError(requestError?.message || 'Unable to save the source timestamp.');
            } finally {
                sourceTimestampsBusy = false;
                if (sourceTimestampSave) sourceTimestampSave.disabled = false;
                renderSourceTimestamps();
            }
        }

        async function openSourceTimestamps() {
            if (saveOutcomeUncertain) { setFooterStatus('Use Reload Task before editing Source Timestamps.', 'error'); return; }
            const step = activeStep();
            const source = step?.kind === 'Source' ? step.source : null;
            if (!source || !sourceTimestampsDialog) return;
            if (!source.collectOnlyNewFiles || !supportsSourceTimestamps(source.hostTypeCode)) {
                setFooterStatus('Source timestamps require a supported Source with Collect only new files enabled.');
                return;
            }
            if (sourceTimestampSourceNeedsSave(step)) {
                setFooterStatus('Save this Source before editing Source Timestamps.');
                return;
            }

            const sourceTimestampHostId = String(source.hostId || '').trim()
                || (String(source.hostTypeCode || '').trim().toLowerCase() === 'filesystem' ? '0' : '');
            sourceTimestampContext = {
                hostId: sourceTimestampHostId,
                hostName: String(source.hostName || source.hostType || 'Source Host').trim(),
                path: String(source.path || '').trim(),
                mask: String(source.fileMask || '*.*').trim() || '*.*'
            };
            if (!sourceTimestampContext.hostId) {
                setFooterStatus('The Source Host is not configured.');
                return;
            }

            sourceTimestamps = [];
            sourceTimestampEditIndex = -1;
            if (sourceTimestampsHost) sourceTimestampsHost.textContent = sourceTimestampContext.hostName;
            if (sourceTimestampsError) { sourceTimestampsError.hidden = true; sourceTimestampsError.textContent = ''; }
            if (sourceTimestampsStatus) sourceTimestampsStatus.textContent = '';
            renderSourceTimestamps();
            if (!sourceTimestampsDialog.open) sourceTimestampsDialog.showModal();
            await loadSourceTimestamps();
        }

        function toggleAddStepMenu() {
            if (!addStepMenu || !addStepButton) return;
            closeActionsMenu();
            closeAddNextActionMenu();
            closeStepActionsMenu();
            closeScheduleActionsMenu();
            closeNextActionActionsMenu();
            const show = addStepMenu.hidden;
            addStepMenu.hidden = !show;
            addStepButton.setAttribute('aria-expanded', show ? 'true' : 'false');
        }

        function closeAddStepMenu() {
            if (addStepMenu) addStepMenu.hidden = true;
            if (addStepButton) addStepButton.setAttribute('aria-expanded', 'false');
        }

        function toggleAddNextActionMenu() {
            if (!addNextActionMenu || !addNextActionButton) return;
            closeActionsMenu();
            closeAddStepMenu();
            closeStepActionsMenu();
            closeScheduleActionsMenu();
            closeNextActionActionsMenu();
            const show = addNextActionMenu.hidden;
            addNextActionMenu.hidden = !show;
            addNextActionButton.setAttribute('aria-expanded', show ? 'true' : 'false');
        }

        function closeAddNextActionMenu() {
            if (addNextActionMenu) addNextActionMenu.hidden = true;
            if (addNextActionButton) addNextActionButton.setAttribute('aria-expanded', 'false');
        }

        function editorFolderBrowseUrl() {
            return String(dialog.dataset.traditionalTaskEditorBrowseUrl || '').trim();
        }

        function folderBrowserTargetStep() {
            if (!folderBrowserTarget) return null;
            return taskSteps.find(item => item.key === folderBrowserTarget.stepKey) || null;
        }

        function folderBrowserTargetModel() {
            const step = folderBrowserTargetStep();
            if (!step || step.kind !== folderBrowserTarget?.kind) return null;
            return step.kind === 'Source' ? step.source : step.kind === 'Destination' ? step.destination : null;
        }

        function closeFolderBrowser() {
            folderBrowser?.close();
            folderBrowser = null;
            folderBrowserTarget = null;
        }

        async function openFolderBrowser(kind, mode = 'folder') {
            const step = activeStep();
            const model = kind === 'Source' && step?.kind === 'Source'
                ? step.source
                : kind === 'Destination' && step?.kind === 'Destination'
                    ? step.destination
                    : null;
            if (!canAlterTask || !step || !model || !window.MauiHostBrowser || folderBrowser?.isOpen()) return;

            const bucketMode = mode === 'bucket';
            const containerMode = mode === 'container';
            const documentLibraryMode = mode === 'document-library';
            const profile = hostWriteProfile(kind, model.hostTypeCode);
            const isS3 = profile === HOST_WRITE_PROFILE_S3;
            const isAzureBlob = profile === HOST_WRITE_PROFILE_AZURE_BLOB;
            const isSharePoint = profile === HOST_WRITE_PROFILE_SHAREPOINT;
            const isGoogleStorage = profile === HOST_WRITE_PROFILE_GOOGLE_STORAGE;
            if (bucketMode && !(isS3 || isGoogleStorage)) return;
            if (containerMode && !isAzureBlob) return;
            if (documentLibraryMode && !isSharePoint) return;

            if (!bucketMode && !containerMode && !documentLibraryMode && containsMoveItMacro(model.path)) {
                setFooterStatus('Folders containing macros cannot be browsed. Remove the macro from Folder(s) before using Browse.');
                const pathControl = kind === 'Source'
                    ? dialog.querySelector('[data-source-field="path"]')
                    : dialog.querySelector('[data-destination-field="path"]');
                pathControl?.focus();
                return;
            }
            if (!model.hostId) {
                setFooterStatus(`Select a ${kind} Host before browsing ${bucketMode ? 'buckets' : containerMode ? 'containers' : documentLibraryMode ? 'document libraries' : 'folders'}.`);
                (kind === 'Source' ? sourceHostSelect : destinationHostSelect)?.focus();
                return;
            }
            if (!canBrowseHostStep(kind, model.hostTypeCode) || !hostBrowseProfile(kind, model.hostTypeCode)) {
                setFooterStatus(`${bucketMode ? 'Bucket' : containerMode ? 'Container' : documentLibraryMode ? 'Document Library' : 'Folder'} browsing is not enabled for ${model.hostType || hostTypeLabel(model.hostTypeCode)} ${kind} steps in this build.`);
                return;
            }
            const browseUrl = editorFolderBrowseUrl();
            if (!browseUrl) {
                setFooterStatus('The Traditional Task editor folder-browse endpoint is not configured.');
                return;
            }

            const effectiveBucket = (isS3 || isGoogleStorage)
                ? String(model.useDefaultBucket ? model.hostDefaults.bucket : model.bucket || '').trim()
                : '';
            const effectiveContainer = isAzureBlob
                ? String(model.useDefaultContainer ? model.hostDefaults.container : model.container || '').trim()
                : '';
            const effectiveDocumentLibrary = isSharePoint
                ? String(model.useDefaultDocumentLibrary ? model.hostDefaults.documentLibrary : model.documentLibrary || '').trim()
                : '';
            const effectiveDocumentLibraryGuid = isSharePoint
                ? String(model.useDefaultDocumentLibrary ? model.hostDefaults.documentLibraryGuid : model.documentLibraryGuid || '').trim()
                : '';
            if ((isS3 || isGoogleStorage) && !bucketMode && !effectiveBucket) {
                setFooterStatus(`Select the ${kind} ${isGoogleStorage ? 'Google Cloud Storage' : 'Amazon S3'} Bucket Name before browsing folders.`);
                (kind === 'Source'
                    ? dialog.querySelector('[data-source-field="bucket"]')
                    : dialog.querySelector('[data-destination-field="bucket"]'))?.focus();
                return;
            }
            if (isAzureBlob && !containerMode && !effectiveContainer) {
                setFooterStatus(`Select the ${kind} Azure Blob Container before browsing folders.`);
                (kind === 'Source'
                    ? dialog.querySelector('[data-source-field="container"]')
                    : dialog.querySelector('[data-destination-field="container"]'))?.focus();
                return;
            }
            if (isAzureBlob && !containerMode && ['$logs', '$blobchangefeed'].includes(effectiveContainer.toLowerCase())) {
                setFooterStatus(`Azure does not allow browsing the ${effectiveContainer} reserved container. Enter the folder path manually if this Step uses that container.`);
                (kind === 'Source'
                    ? dialog.querySelector('[data-source-field="container"]')
                    : dialog.querySelector('[data-destination-field="container"]'))?.focus();
                return;
            }
            if (isSharePoint && !documentLibraryMode && (!effectiveDocumentLibrary || !effectiveDocumentLibraryGuid)) {
                setFooterStatus(`Select the ${kind} SharePoint Document Library before browsing folders.`);
                (kind === 'Source'
                    ? dialog.querySelector('[data-source-field="documentLibrary"]')
                    : dialog.querySelector('[data-destination-field="documentLibrary"]'))?.focus();
                return;
            }

            const rawEditorPath = String(model.path || '').trim();
            folderBrowserTarget = { typeCode: model.hostTypeCode };
            const preserveBlankPath = isSharePoint && rawEditorPath.length === 0;
            folderBrowserTarget = {
                stepKey: step.key,
                typeCode: model.hostTypeCode,
                kind,
                mode: bucketMode ? 'bucket' : containerMode ? 'container' : documentLibraryMode ? 'document-library' : 'folder',
                bucket: effectiveBucket,
                container: effectiveContainer,
                documentLibrary: effectiveDocumentLibrary,
                documentLibraryGuid: effectiveDocumentLibraryGuid,
                requestPath: isSharePoint
                    ? rawEditorPath
                    : isGoogleStorage && bucketMode
                        ? (rawEditorPath || '/')
                        : normalizeEditorFolderPath(rawEditorPath || '/'),
                preserveBlankPath,
                stepId: String(model.id || step.id || '').trim()
            };
            const path = isSharePoint ? rawEditorPath : (bucketMode || containerMode || documentLibraryMode) ? '/' : normalizeEditorFolderPath(rawEditorPath || '/');
            folderBrowser = window.MauiHostBrowser.open({
                title: bucketMode ? `Browsing buckets on ${model.hostType}` : containerMode ? `Browsing containers on ${model.hostType}`
                    : documentLibraryMode ? `Browsing document libraries on ${model.hostType}` : `Browsing folders on ${model.hostType || hostTypeLabel(model.hostTypeCode)}`,
                hostName: model.hostName || `Host ID ${model.hostId}`,
                path, selector: bucketMode || containerMode || documentLibraryMode,
                hint: bucketMode || containerMode || documentLibraryMode ? 'Select an item and click OK, or double-click it to select.' : undefined,
                load: loadFolderBrowser,
                onSelect: applyFolderBrowserSelection,
                onClose: () => { folderBrowserTarget = null; folderBrowser = null; }
            });
            if (!folderBrowser) folderBrowserTarget = null;
        }

        function normalizeEditorFolderPath(value) {
            if (folderBrowserTarget?.typeCode === 'FileSystem') {
                let local = String(value || '').trim().replaceAll('/', '\\');
                if (!local || local === '\\') return '/';
                local = local.replace(/^\\(?=[a-z]:)/i, '');
                if (/^[a-z]:$/i.test(local)) return `${local}\\`;
                if (/^[a-z]:\\$/i.test(local)) return local;
                return local.replace(/\\+$/, '');
            }
            let path = String(value || '').trim().replaceAll('\\', '/');
            while (path.includes('//')) path = path.replaceAll('//', '/');
            if (!path || path === '/') return '/';
            return `/${path.replace(/^\/+|\/+$/g, '')}`;
        }

        async function loadFolderBrowser(path, signal) {
            const target = folderBrowserTarget, model = folderBrowserTargetModel();
            if (!target || !model) throw new Error('The selected Task Step is no longer available.');
            const isSelectorMode = ['bucket', 'container', 'document-library'].includes(target.mode);
            const rawPath = String(path ?? '').trim();
            const requestedPath = target.preserveBlankPath && !rawPath ? '' : normalizeEditorFolderPath(rawPath || '/');
            const url = new URL(editorFolderBrowseUrl(), window.location.href);
            Object.entries({ taskId: activeTaskId, hostId: model.hostId, kind: target.kind,
                path: isSelectorMode ? target.requestPath ?? '/' : requestedPath, stepId: target.stepId,
                bucket: target.bucket, container: target.container, documentLibrary: target.documentLibrary,
                documentLibraryGuid: target.documentLibraryGuid }).forEach(([key, value]) => { if (value != null) url.searchParams.set(key, value); });
            if (target.mode === 'bucket') url.searchParams.set('browseBuckets', 'true');
            if (target.mode === 'container') url.searchParams.set('browseContainers', 'true');
            if (target.mode === 'document-library') url.searchParams.set('browseDocumentLibraries', 'true');
            const data = await fetchJson(url.toString(), { signal });
            const currentPath = target.preserveBlankPath && !String(data.currentPath ?? '') && !requestedPath
                ? '' : normalizeEditorFolderPath(data.currentPath || requestedPath || '/');
            return { currentPath, hostName: data.hostName,
                parentPath: isSelectorMode ? '' : data.parentPath ? normalizeEditorFolderPath(data.parentPath) : '',
                canSelectCurrent: !isSelectorMode,
                items: (data.items || []).map(item => ({ ...item, path: item.isFolder ? normalizeEditorFolderPath(item.path) : '' })) };
        }

        function applyFolderBrowserSelection(selection) {
            const step = folderBrowserTargetStep();
            const rawChosenPath = String(selection.path ?? '').trim();
            const chosenPath = folderBrowserTarget?.preserveBlankPath && rawChosenPath.length === 0
                ? ''
                : normalizeEditorFolderPath(rawChosenPath || '/');
            if (!step) return;
            activeStepKey = step.key;
            const bucketMode = folderBrowserTarget?.mode === 'bucket';
            const containerMode = folderBrowserTarget?.mode === 'container';
            const documentLibraryMode = folderBrowserTarget?.mode === 'document-library';
            if (bucketMode || containerMode || documentLibraryMode) {
                const chosenCloudName = chosenPath.replace(/^\/+|\/+$/g, '');
                if (!chosenCloudName) return;
                const fieldName = bucketMode ? 'bucket' : containerMode ? 'container' : 'documentLibrary';
                const selectedItem = selection.item || null;
                if (step.kind === 'Source') {
                    const control = dialog.querySelector(`[data-source-field="${fieldName}"]`);
                    if (control) {
                        control.value = chosenCloudName;
                        if (documentLibraryMode) step.source.documentLibraryGuid = String(selectedItem?.id || '');
                        applySourceControl(control, true);
                        if (documentLibraryMode) {
                            step.source.documentLibraryGuid = String(selectedItem?.id || '');
                            step.source.useDefaultDocumentLibrary = chosenCloudName === String(step.source.hostDefaults.documentLibrary || '').trim()
                                && step.source.documentLibraryGuid === String(step.source.hostDefaults.documentLibraryGuid || '').trim();
                            renderSource(step);
                        }
                    }
                } else if (step.kind === 'Destination') {
                    const control = dialog.querySelector(`[data-destination-field="${fieldName}"]`);
                    if (control) {
                        control.value = chosenCloudName;
                        if (documentLibraryMode) step.destination.documentLibraryGuid = String(selectedItem?.id || '');
                        applyDestinationControl(control, true);
                        if (documentLibraryMode) {
                            step.destination.documentLibraryGuid = String(selectedItem?.id || '');
                            step.destination.useDefaultDocumentLibrary = chosenCloudName === String(step.destination.hostDefaults.documentLibrary || '').trim()
                                && step.destination.documentLibraryGuid === String(step.destination.hostDefaults.documentLibraryGuid || '').trim();
                            renderDestination(step);
                        }
                    }
                }
            } else {
                const source = step.kind === 'Source';
                const model = source ? step.source : step.destination;
                const before = deepClone(model);
                if (source) applySourceValue(model, 'path', chosenPath); else applyDestinationValue(model, 'path', chosenPath);
                if (selection.fileName !== null) {
                    if (source) model.fileMask = selection.fileName;
                    else { model.fileName = selection.fileName; model.useOriginalName = false; }
                }
                if (!sameValue(before, model)) {
                    lastUndo = { kind: source ? 'source-snapshot' : 'destination-snapshot', stepKey: step.key, before, label: 'Browse selection' };
                    if (source) { seedSourceStableValues(step); renderSource(step); }
                    else { seedDestinationStableValues(step); renderDestination(step); }
                    updateStepSummary(step); updateUndoState(); updateDirtyState();
                }
            }
            closeFolderBrowser();
        }

        function showError(message) {
            loading.hidden = true;
            content.hidden = true;
            error.textContent = friendlyEditorError(message || 'Unable to load the Traditional Task Editor.');
            error.hidden = false;
        }

        function setText(selector, value) {
            const element = dialog.querySelector(selector);
            if (element) element.textContent = value ?? '';
        }

        function setValue(selector, value) {
            const element = dialog.querySelector(selector);
            if (element) element.value = value ?? '';
        }

        function setSelectValue(selector, value) {
            const element = dialog.querySelector(selector);
            if (!element) return;
            const normalized = String(value ?? '');
            if (normalized && ![...element.options].some(option => option.value.toLowerCase() === normalized.toLowerCase())) {
                element.appendChild(new Option(normalized, normalized));
            }
            const match = [...element.options].find(option => option.value.toLowerCase() === normalized.toLowerCase());
            element.value = match?.value ?? '';
        }

        function setChecked(selector, value) {
            const element = dialog.querySelector(selector);
            if (element instanceof HTMLInputElement) element.checked = Boolean(value);
        }

        function normalizeCacheNames(value) {
            const normalized = String(value || '').trim().toLowerCase();
            if (normalized === 'orig' || normalized === 'random') return normalized;
            return 'random';
        }

        function formatDate(value) {
            if (!value) return '—';
            const date = new Date(value);
            return Number.isNaN(date.valueOf()) ? String(value) : date.toLocaleString();
        }

        function nullableNumber(value) {
            if (value == null || value === '') return null;
            const parsed = Number(value);
            return Number.isFinite(parsed) ? parsed : null;
        }

        function nullableBoolean(value) {
            if (value == null || value === '') return null;
            if (typeof value === 'boolean') return value;
            const normalized = String(value).toLowerCase();
            if (normalized === 'true' || normalized === '1') return true;
            if (normalized === 'false' || normalized === '0') return false;
            return null;
        }

        function windowsCopyFileEnabled(apiMode) {
            const normalized = nullableBoolean(apiMode);
            return normalized == null ? null : !normalized;
        }

        function effectiveOverrideValue(override, localValue, defaultValue) {
            return override ? localValue : defaultValue;
        }

        function formatValue(value) {
            if (value == null || value === '') return 'not specified';
            if (typeof value === 'boolean') return value ? 'On' : 'Off';
            return String(value);
        }

        function sameValue(left, right) {
            return JSON.stringify(left) === JSON.stringify(right);
        }

        function cloneSimple(value) {
            return value && typeof value === 'object' ? deepClone(value) : value;
        }

        function deepClone(value) {
            return value == null ? value : JSON.parse(JSON.stringify(value));
        }

        saveFlowAction?.addEventListener('click', saveTaskFlowImage);
        tabs.forEach(tab => tab.addEventListener('click', () => setTab(tab.dataset.traditionalTaskEditorTab || 'task')));
        dialog.querySelectorAll('[data-traditional-task-editor-section-button]').forEach(button => button.addEventListener('click', () => { const section=button.dataset.traditionalTaskEditorSectionButton || 'steps'; if(isAdvancedTask() && section==='steps' && advancedEditor?.capture()) advancedEditor.restore(advancedEditor.capture()); else setWorkspace(section); }));
        sourceTabs.forEach(tab => tab.addEventListener('click', () => {
            const name = tab.dataset.traditionalTaskEditorSourceTab || 'general';
            if (name === 'host-override') {
                hostTabName = '';
                const source = activeSource();
                if (source) configureHostOverrideTabs(source);
            }
            setSourceTab(name);
        }));
        processTabs.forEach(tab => tab.addEventListener('click', () => setProcessTab(tab.dataset.traditionalTaskEditorProcessTab || 'general')));
        destinationTabs.forEach(tab => tab.addEventListener('click', () => {
            const name = tab.dataset.traditionalTaskEditorDestinationTab || 'general';
            if (name === 'host-override') {
                destinationHostTabName = '';
                const destination = activeStep()?.destination;
                if (destination) configureDestinationHostOverrideTabs(destination);
            }
            setDestinationTab(name);
        }));
        destinationHostTabs.forEach(tab => tab.addEventListener('click', () => { if (!tab.hidden) setDestinationHostTab(tab.dataset.traditionalTaskEditorDestinationHostTab || 'timeouts'); }));
        hostTabs.forEach(tab => tab.addEventListener('click', () => { if (!tab.hidden) setHostTab(tab.dataset.traditionalTaskEditorHostTab || 'timeouts'); }));
        scheduleTabs.forEach(tab => tab.addEventListener('click', () => setScheduleTab(tab.dataset.traditionalTaskEditorScheduleTab || 'days')));
        scheduleDayModeButtons.forEach(button => button.addEventListener('click', () => setScheduleDayMode(button.dataset.traditionalTaskEditorScheduleDayMode || 'week')));
        nextActionTabs.forEach(tab => tab.addEventListener('click', () => { if (!tab.hidden) setNextActionTab(tab.dataset.traditionalTaskEditorNextActionTab || 'general'); }));

        stepItems?.addEventListener('click', event => {
            const button = event.target.closest?.('[data-traditional-task-editor-step-key]');
            if (!button) return;
            selectStep(button.dataset.traditionalTaskEditorStepKey || '');
        });

        scheduleItems?.addEventListener('click', event => {
            const button = event.target.closest?.('[data-traditional-task-editor-schedule-key]');
            if (!button) return;
            selectSchedule(button.dataset.traditionalTaskEditorScheduleKey || '');
        });

        nextActionItems?.addEventListener('click', event => {
            const button = event.target.closest?.('[data-traditional-task-editor-next-action-key]');
            if (!button) return;
            selectNextAction(button.dataset.traditionalTaskEditorNextActionKey || '');
        });

        dialog.addEventListener('input', event => {
            const sourceControl = event.target.closest?.('[data-source-field]');
            if (sourceControl) {
                const isDiscrete = sourceControl instanceof HTMLSelectElement || (sourceControl instanceof HTMLInputElement && ['checkbox', 'radio'].includes(sourceControl.type));
                if (!isDiscrete) applySourceControl(sourceControl, false);
                return;
            }
            const processControl = event.target.closest?.('[data-process-field]');
            if (processControl) {
                const isDiscrete = processControl instanceof HTMLSelectElement || (processControl instanceof HTMLInputElement && ['checkbox', 'radio'].includes(processControl.type));
                if (!isDiscrete) applyProcessControl(processControl, false);
                return;
            }
            const destinationControl = event.target.closest?.('[data-destination-field]');
            if (destinationControl) {
                const isDiscrete = destinationControl instanceof HTMLSelectElement || (destinationControl instanceof HTMLInputElement && ['checkbox', 'radio'].includes(destinationControl.type));
                if (!isDiscrete) applyDestinationControl(destinationControl, false);
                return;
            }
            const nextActionControl = event.target.closest?.('[data-next-action-field]');
            if (nextActionControl) {
                const isDiscrete = nextActionControl instanceof HTMLSelectElement || (nextActionControl instanceof HTMLInputElement && ['checkbox', 'radio'].includes(nextActionControl.type));
                if (!isDiscrete) applyNextActionControl(nextActionControl, false);
                return;
            }
            const control = event.target.closest?.('[data-editor-field]');
            if (!control) return;
            refreshLinkedPresentation(control);
            updateDirtyState();
        });

        dialog.addEventListener('change', async event => {
            const scheduleOptionControl = event.target.closest?.('[data-schedule-field]');
            if (scheduleOptionControl) {
                applyScheduleOptionControl(scheduleOptionControl);
                return;
            }
            const scheduleIntervalControl = event.target.closest?.('[data-schedule-interval-field]');
            if (scheduleIntervalControl) {
                applyScheduleIntervalControl(scheduleIntervalControl);
                return;
            }
            const scheduleDateListControl = event.target.closest?.('[data-schedule-date-list-field]');
            if (scheduleDateListControl) {
                applyScheduleDateListControl(scheduleDateListControl);
                return;
            }
            const sourceControl = event.target.closest?.('[data-source-field]');
            if (sourceControl) {
                if (sourceControl.dataset.sourceField === 'hasSpecialFilters' && sourceControl.checked) {
                    const source = activeSource();
                    const hasEditableFilters = Boolean(source?.specialFilters && !source.specialFilters.hasUnsupportedCriteria && source.specialFilters.filters?.length);
                    if (!hasEditableFilters) {
                        sourceControl.checked = Boolean(source?.hasSpecialFilters);
                        showSpecialFiltersEditor();
                        return;
                    }
                }
                if (!await confirmHostChange(sourceControl, 'Source')) return;
                applySourceControl(sourceControl, true);
                return;
            }
            const processControl = event.target.closest?.('[data-process-field]');
            if (processControl) {
                applyProcessControl(processControl, true);
                return;
            }
            const destinationPermissionControl = event.target.closest?.('[data-destination-permission]');
            if (destinationPermissionControl) {
                applyDestinationPermissionControl(destinationPermissionControl);
                return;
            }
            const destinationControl = event.target.closest?.('[data-destination-field]');
            if (destinationControl) {
                if (destinationControl.dataset.destinationField === 'isHtml' && activeSmtpDestination()) {
                    applyDestinationSmtpFormat(destinationControl);
                    return;
                }
                if (!await confirmHostChange(destinationControl, 'Destination')) return;
                applyDestinationControl(destinationControl, true);
                return;
            }
            const nextActionControl = event.target.closest?.('[data-next-action-field]');
            if (nextActionControl) {
                applyNextActionControl(nextActionControl, true);
                return;
            }
            const control = event.target.closest?.('[data-editor-field]');
            if (control) commitField(control);
        });

        dialog.addEventListener('focusout', event => {
            const sourceControl = event.target.closest?.('[data-source-field]');
            if (sourceControl && !(sourceControl instanceof HTMLInputElement && ['checkbox', 'radio'].includes(sourceControl.type)) && !(sourceControl instanceof HTMLSelectElement)) {
                applySourceControl(sourceControl, true);
                return;
            }
            const processControl = event.target.closest?.('[data-process-field]');
            if (processControl && !(processControl instanceof HTMLInputElement && ['checkbox', 'radio'].includes(processControl.type)) && !(processControl instanceof HTMLSelectElement)) {
                applyProcessControl(processControl, true);
                return;
            }
            const destinationControl = event.target.closest?.('[data-destination-field]');
            if (destinationControl && !(destinationControl instanceof HTMLInputElement && ['checkbox', 'radio'].includes(destinationControl.type)) && !(destinationControl instanceof HTMLSelectElement)) {
                applyDestinationControl(destinationControl, true);
                return;
            }
            const nextActionControl = event.target.closest?.('[data-next-action-field]');
            if (nextActionControl && !(nextActionControl instanceof HTMLInputElement && ['checkbox', 'radio'].includes(nextActionControl.type)) && !(nextActionControl instanceof HTMLSelectElement)) {
                applyNextActionControl(nextActionControl, true);
                return;
            }
            const control = event.target.closest?.('[data-editor-field]');
            if (control && !(control instanceof HTMLInputElement && control.type === 'checkbox') && !(control instanceof HTMLSelectElement)) commitField(control);
        });

        sourceHostTypeFilter?.addEventListener('change', () => changeHostTypeFilter('Source', sourceHostTypeFilter));
        destinationHostTypeFilter?.addEventListener('change', () => changeHostTypeFilter('Destination', destinationHostTypeFilter));

        stateCacheModeControls.forEach(control => control.addEventListener('change', () => {
            if (control.checked) commitStateCacheMode();
        }));
        stateCacheForValue?.addEventListener('change', commitStateCacheForValue);
        stateCacheForValue?.addEventListener('focusout', commitStateCacheForValue);
        useDefaultStateCache?.addEventListener('change', refreshAdvancedDependencies);
        useGlobalTaskLogRetention?.addEventListener('change', refreshAdvancedDependencies);

        dialog.addEventListener('cancel', event => { event.preventDefault(); requestClose(); });
        dialog.addEventListener('close', () => { if (dialog.open) return; if (!editingHost) void checkout?.close(); window.clearTimeout(autoSaveTimer); logsRequest?.abort(); logsRequest = null; logsGeneration++; try { onClosed?.(activeTaskId, activeTaskName); } catch (closeError) { console.warn('MAUI Task Editor close callback failed.', closeError); } });

        logsRows?.addEventListener('click', event => {
            const button = event.target.closest?.('[data-traditional-task-editor-log-id]');
            if (!button) return;
            openTaskLogViewer(button.dataset.traditionalTaskEditorLogId);
        });

        undoButton?.addEventListener('click', requestUndoLastEdit);
        actionsButton?.addEventListener('click', event => { event.stopPropagation(); toggleActionsMenu(); });
        createAdvancedAction?.addEventListener('click', createAdvancedCurrentTask);

        dialog.querySelectorAll('[data-sync-action]').forEach(button => button.addEventListener('click', () => runSyncAction(button.dataset.syncAction)));
        dialog.querySelector('[data-sync-view-preview]')?.addEventListener('click', openSyncPreview);
        syncPreviewDialog?.querySelectorAll('[data-sync-preview-close]').forEach(button => button.addEventListener('click', closeSyncPreview));
        syncPreviewDialog?.addEventListener('cancel', event => { event.preventDefault(); closeSyncPreview(); });
        syncPreviewDialog?.querySelector('[data-sync-preview-refresh]').addEventListener('click', refreshSyncPreview);
        syncPreviewDialog?.querySelector('[data-sync-preview-show-no-action]').addEventListener('change', renderSyncPreview);
        syncPreviewDialog?.querySelector('[data-sync-preview-initial]').addEventListener('click', openSyncInitialOptions);
        syncPreviewDialog?.querySelector('[data-sync-preview-ok]').addEventListener('click', async () => {
            if (!canAlterTask) { closeSyncPreview(); return; }
            if (await postSyncAction('accept-preview')) closeSyncPreview();
        });
        syncInitialDialog?.querySelectorAll('[data-sync-initial-close]').forEach(button => button.addEventListener('click', () => { if (!syncInitialDialog.inert) syncInitialDialog.close(); }));
        syncInitialDialog?.addEventListener('cancel', event => { event.preventDefault(); if (!syncInitialDialog.inert) syncInitialDialog.close(); });
        syncInitialDialog?.querySelectorAll('[name="maui-sync-initial-mode"]').forEach(control => control.addEventListener('change', refreshSyncInitialOptions));
        syncInitialDialog?.querySelector('[data-sync-initial-save]').addEventListener('click', saveSyncInitialOptions);
        preflightAction?.addEventListener('click', () => runSavedTaskAction(onPreflight));
        runNowAction?.addEventListener('click', async () => {
            if (!currentCanRun || runNowAction.disabled) return;
            const result = await runSavedTaskAction(onRunNow);
            if (result?.message) setFooterStatus(result.message, result.success === false ? 'error' : 'default');
        });
        exportAction?.addEventListener('click', () => runSavedTaskAction(onExport));
        deleteAction?.addEventListener('click', deleteCurrentTask);
        cloneAction?.addEventListener('click', cloneCurrentTask);
        taskParametersButton?.addEventListener('click', openTaskParameters);
        transferExceptionsButton?.addEventListener('click', openTransferExceptions);
        stepActionsButton?.addEventListener('click', event => { event.stopPropagation(); toggleStepActionsMenu(); });
        stepActionButtons.forEach(button => button.addEventListener('click', () => handleStepAction(button.dataset.traditionalTaskEditorStepAction || '')));
        addStepButton?.addEventListener('click', event => { event.stopPropagation(); if (isSyncTask()) addSource(); else toggleAddStepMenu(); });
        addSourceButton?.addEventListener('click', addSource);
        addProcessButton?.addEventListener('click', addProcess);
        addDestinationButton?.addEventListener('click', addDestination);
        scheduleAddButton?.addEventListener('click', addSchedule);
        scheduleActionsButton?.addEventListener('click', event => { event.stopPropagation(); toggleScheduleActionsMenu(); });
        scheduleActionButtons.forEach(button => button.addEventListener('click', () => handleScheduleAction(button.dataset.traditionalTaskEditorScheduleAction || '')));
        scheduleAddDateListButton?.addEventListener('click', addScheduleDateList);
        scheduleAddTimeButton?.addEventListener('click', addScheduleTime);
        dialog.querySelectorAll('[data-schedule-weekday]').forEach(button => button.addEventListener('click', () => toggleScheduleWeekday(button.dataset.scheduleWeekday || '')));
        dialog.querySelectorAll('[data-schedule-monthday]').forEach(button => button.addEventListener('click', () => toggleScheduleMonthDay(button.dataset.scheduleMonthday || '')));
        scheduleDateListRows?.addEventListener('click', event => {
            const button = event.target.closest?.('[data-schedule-date-list-remove]');
            if (button) removeScheduleDateList(button.dataset.scheduleDateListRemove || '');
        });
        scheduleIntervals?.addEventListener('click', event => {
            const button = event.target.closest?.('[data-schedule-interval-remove]');
            if (button) removeScheduleTime(button.dataset.scheduleIntervalRemove || '');
        });
        addNextActionButton?.addEventListener('click', event => { event.stopPropagation(); toggleAddNextActionMenu(); });
        addEmailActionButton?.addEventListener('click', () => addNextAction('Email'));
        addRunTaskActionButton?.addEventListener('click', () => addNextAction('Task'));
        nextActionActionsButton?.addEventListener('click', event => { event.stopPropagation(); toggleNextActionActionsMenu(); });
        nextActionActionButtons.forEach(button => button.addEventListener('click', () => handleNextActionAction(button.dataset.traditionalTaskEditorNextActionAction || '')));
        nextActionAddParameter?.addEventListener('click', () => openNextActionParameterEditor());
        nextActionParameterRows?.addEventListener('click', event => {
            const edit = event.target.closest?.('[data-traditional-task-editor-next-action-parameter-edit]');
            if (edit) {
                openNextActionParameterEditor(edit.dataset.traditionalTaskEditorNextActionParameterEdit || '');
                return;
            }
            const remove = event.target.closest?.('[data-traditional-task-editor-next-action-parameter-remove]');
            if (remove) removeNextActionParameter(remove.dataset.traditionalTaskEditorNextActionParameterRemove || '');
        });
        nextActionParameterSave?.addEventListener('click', applyNextActionParameterEditor);
        nextActionParameterCancelButtons.forEach(button => button.addEventListener('click', () => {
            nextActionParameterEditName = '';
            nextActionParameterDialog?.close();
        }));
        nextActionParameterDialog?.addEventListener('cancel', event => {
            event.preventDefault();
            nextActionParameterEditName = '';
            nextActionParameterDialog.close();
        });
        nextActionSaveTemplate?.addEventListener('click', () => saveEditorNotificationTemplate('next-action'));
        fieldMacroSelects.forEach(select => select.addEventListener('change', () => applyFieldMacro(select)));
        sourceBrowseButton?.addEventListener('click', () => openFolderBrowser('Source'));
        sourceBucketBrowseButton?.addEventListener('click', () => openFolderBrowser('Source', 'bucket'));
        sourceContainerBrowseButton?.addEventListener('click', () => openFolderBrowser('Source', 'container'));
        sourceDocumentLibraryBrowseButton?.addEventListener('click', () => openFolderBrowser('Source', 'document-library'));
        destinationBrowseButton?.addEventListener('click', () => openFolderBrowser('Destination'));
        destinationBucketBrowseButton?.addEventListener('click', () => openFolderBrowser('Destination', 'bucket'));
        destinationContainerBrowseButton?.addEventListener('click', () => openFolderBrowser('Destination', 'container'));
        destinationDocumentLibraryBrowseButton?.addEventListener('click', () => openFolderBrowser('Destination', 'document-library'));
        processAddParameter?.addEventListener('click', () => openProcessParameterEditor());
        processParameterRows?.addEventListener('click', event => {
            const edit = event.target.closest?.('[data-traditional-task-editor-process-parameter-edit]');
            if (edit) {
                openProcessParameterEditor(edit.dataset.traditionalTaskEditorProcessParameterEdit || '');
                return;
            }
            const remove = event.target.closest?.('[data-traditional-task-editor-process-parameter-remove]');
            if (remove) removeProcessParameter(remove.dataset.traditionalTaskEditorProcessParameterRemove || '');
        });
        processParameterSave?.addEventListener('click', saveProcessParameter);
        processParameterReset?.addEventListener('click', resetProcessParameterValue);
        processParameterCancelButtons.forEach(button => button.addEventListener('click', () => processParameterDialog?.close()));
        processParameterDialog?.addEventListener('cancel', event => { event.preventDefault(); processParameterDialog.close(); });
        editHostButtons.forEach(button => button.addEventListener('click', () => editSelectedHost(button.dataset.traditionalTaskEditorEditHost, button)));
        specialFilterMatchControls.forEach(control => control.addEventListener('change', () => {
            if (specialFilterDraft && control.checked) specialFilterDraft.matchMode = control.value === 'any' ? 'any' : 'all';
        }));
        specialFilterMore?.addEventListener('click', () => {
            if (!specialFilterDraft || specialFilterDraft.hasUnsupportedCriteria || specialFilterDraft.filters.length >= 6) return;
            specialFilterDraft.filters.push(defaultSpecialFilterCriterion());
            renderSpecialFilterRows();
        });
        specialFiltersApply?.addEventListener('click', applySpecialFiltersEditor);
        specialFiltersCancelButtons.forEach(button => button.addEventListener('click', closeSpecialFiltersEditor));
        specialFiltersDialog?.addEventListener('cancel', event => { event.preventDefault(); closeSpecialFiltersEditor(); });
        specialFiltersButton?.addEventListener('click', showSpecialFiltersEditor);
        closeButton?.addEventListener('click', requestClose);
        cancelButton?.addEventListener('click', requestClose);
        parametersDialog?.querySelectorAll('[data-traditional-task-editor-parameters-close]').forEach(button => button.addEventListener('click', () => parametersDialog.close()));
        parametersDialog?.addEventListener('cancel', event => { event.preventDefault(); parametersDialog.close(); });
        parameterRows?.addEventListener('click', event => {
            const edit = event.target.closest?.('[data-traditional-task-editor-parameter-edit]');
            if (edit) { openTaskParameterEditor(edit.dataset.traditionalTaskEditorParameterEdit || ''); return; }
            const remove = event.target.closest?.('[data-traditional-task-editor-parameter-remove]');
            if (remove) removeTaskParameter(remove.dataset.traditionalTaskEditorParameterRemove || '');
        });
        addParameterButton?.addEventListener('click', openAddParameter);
        taskParameterSave?.addEventListener('click', applyTaskParameterEditor);
        addParameterDialog?.querySelectorAll('[data-traditional-task-editor-add-parameter-close]').forEach(button => button.addEventListener('click', () => { taskParameterEditName = ''; addParameterDialog.close(); }));
        addParameterDialog?.addEventListener('cancel', event => { event.preventDefault(); addParameterDialog.close(); });
        transferExceptionsDialog?.querySelectorAll('[data-traditional-task-editor-transfer-exceptions-close]').forEach(button => button.addEventListener('click', () => transferExceptionsDialog.close()));
        transferExceptionsDialog?.addEventListener('cancel', event => { event.preventDefault(); transferExceptionsDialog.close(); });
        transferExceptionsRows?.addEventListener('click', event => {
            const remove = event.target.closest?.('[data-transfer-exception-remove]');
            if (remove) removeTransferException(remove.dataset.transferExceptionRemove || '');
        });
        sourceTimestampsDialog?.querySelectorAll('[data-traditional-task-editor-source-timestamps-close]').forEach(button => button.addEventListener('click', () => sourceTimestampsDialog.close()));
        sourceTimestampsDialog?.addEventListener('cancel', event => { event.preventDefault(); sourceTimestampsDialog.close(); });
        sourceTimestampAdd?.addEventListener('click', () => openSourceTimestampEditor(-1));
        sourceTimestampsRows?.addEventListener('click', event => {
            const edit = event.target.closest?.('[data-traditional-task-editor-source-timestamp-edit]');
            if (!edit) return;
            const index = Number(edit.dataset.traditionalTaskEditorSourceTimestampEdit);
            if (Number.isInteger(index)) openSourceTimestampEditor(index);
        });
        sourceTimestampSave?.addEventListener('click', saveSourceTimestamp);
        sourceTimestampEditCancelButtons.forEach(button => button.addEventListener('click', () => {
            sourceTimestampEditIndex = -1;
            sourceTimestampEditDialog?.close();
        }));
        sourceTimestampEditDialog?.addEventListener('cancel', event => {
            event.preventDefault();
            sourceTimestampEditIndex = -1;
            sourceTimestampEditDialog.close();
        });
        sourceTimestampValue?.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                saveSourceTimestamp();
            }
        });

        destinationSmtpHtmlEditor?.addEventListener('keyup', rememberDestinationSmtpHtmlRange);
        destinationSmtpHtmlEditor?.addEventListener('mouseup', rememberDestinationSmtpHtmlRange);
        destinationSmtpHtmlEditor?.addEventListener('focus', rememberDestinationSmtpHtmlRange);
        destinationSmtpHtmlEditor?.addEventListener('input', () => updateDestinationSmtpMessage(false));
        destinationSmtpHtmlEditor?.addEventListener('focusout', () => updateDestinationSmtpMessage(true));
        destinationSmtpHtmlSource?.addEventListener('input', () => updateDestinationSmtpMessage(false));
        destinationSmtpHtmlSource?.addEventListener('focusout', () => updateDestinationSmtpMessage(true));
        destinationSmtpTextEditor?.addEventListener('input', () => updateDestinationSmtpMessage(false));
        destinationSmtpTextEditor?.addEventListener('focusout', () => updateDestinationSmtpMessage(true));
        dialog.querySelectorAll('[data-destination-smtp-command]').forEach(button => {
            button.addEventListener('mousedown', event => event.preventDefault());
            button.addEventListener('click', () => applyDestinationSmtpMessageMutation('Message', () => {
                restoreDestinationSmtpHtmlRange();
                document.execCommand(button.dataset.destinationSmtpCommand, false, null);
                rememberDestinationSmtpHtmlRange();
            }));
        });
        destinationSmtpHtmlToggle?.addEventListener('mousedown', event => event.preventDefault());
        destinationSmtpHtmlToggle?.addEventListener('click', () => {
            const destination = activeSmtpDestination();
            if (!destination?.isHtml) return;
            if (destinationSmtpHtmlSourceMode) {
                applyDestinationSmtpMessageMutation('Message', () => setDestinationSmtpHtmlSourceMode(false, false));
            } else {
                setDestinationSmtpHtmlSourceMode(true);
            }
        });

        nextActionHtmlEditor?.addEventListener('keyup', rememberNextActionHtmlRange);
        nextActionHtmlEditor?.addEventListener('mouseup', rememberNextActionHtmlRange);
        nextActionHtmlEditor?.addEventListener('focus', rememberNextActionHtmlRange);
        dialog.querySelectorAll('[data-next-action-command]').forEach(button => {
            button.addEventListener('mousedown', event => event.preventDefault());
            button.addEventListener('click', () => applyNextActionMessageMutation('Message Body', () => {
                restoreNextActionHtmlRange();
                document.execCommand(button.dataset.nextActionCommand, false, null);
                rememberNextActionHtmlRange();
            }));
        });
        nextActionHtmlToggle?.addEventListener('mousedown', event => event.preventDefault());
        nextActionHtmlToggle?.addEventListener('click', () => {
            const action = activeNextAction();
            if (!action?.isHtml) return;
            if (nextActionHtmlSourceMode) applyNextActionMessageMutation('Message Body', () => setNextActionHtmlSourceMode(false, false));
            else setNextActionHtmlSourceMode(true);
        });
        function closeNextActionLinkDialog() {
            if (nextActionLinkDialog?.open) nextActionLinkDialog.close();
            emailLinkEditorContext = '';
        }
        function prepareEmailLinkDialog(context) {
            if (!nextActionLinkDialog) return false;
            emailLinkEditorContext = context;
            if (nextActionLinkError) { nextActionLinkError.hidden = true; nextActionLinkError.textContent = ''; }
            if (nextActionLinkUrl) nextActionLinkUrl.value = '';
            nextActionLinkDialog.showModal();
            window.setTimeout(() => nextActionLinkUrl?.focus(), 0);
            return true;
        }
        function openNextActionLinkDialog() {
            const action = activeNextAction();
            if (!action?.isHtml || nextActionHtmlSourceMode) return;
            rememberNextActionHtmlRange();
            prepareEmailLinkDialog('next-action');
        }
        function openDestinationSmtpLinkDialog() {
            const destination = activeSmtpDestination();
            if (!destination?.isHtml || destinationSmtpHtmlSourceMode) return;
            rememberDestinationSmtpHtmlRange();
            prepareEmailLinkDialog('smtp-destination');
        }
        function addNextActionLink() {
            const url = String(nextActionLinkUrl?.value || '').trim();
            if (!/^(https?:|mailto:)/i.test(url)) {
                if (nextActionLinkError) { nextActionLinkError.textContent = 'Enter a URL beginning with http://, https://, or mailto:.'; nextActionLinkError.hidden = false; }
                nextActionLinkUrl?.focus();
                return;
            }
            const context = emailLinkEditorContext;
            closeNextActionLinkDialog();
            if (context === 'smtp-destination') {
                applyDestinationSmtpMessageMutation('Message', () => {
                    restoreDestinationSmtpHtmlRange();
                    document.execCommand('createLink', false, url);
                    rememberDestinationSmtpHtmlRange();
                });
                return;
            }
            applyNextActionMessageMutation('Message Body', () => {
                restoreNextActionHtmlRange();
                document.execCommand('createLink', false, url);
                rememberNextActionHtmlRange();
            });
        }
        destinationSmtpLinkButton?.addEventListener('mousedown', event => event.preventDefault());
        destinationSmtpLinkButton?.addEventListener('click', openDestinationSmtpLinkDialog);
        nextActionLinkButton?.addEventListener('mousedown', event => event.preventDefault());
        nextActionLinkButton?.addEventListener('click', openNextActionLinkDialog);
        nextActionLinkConfirm?.addEventListener('click', addNextActionLink);
        nextActionLinkCancelButtons.forEach(button => button.addEventListener('click', closeNextActionLinkDialog));
        nextActionLinkDialog?.addEventListener('cancel', event => { event.preventDefault(); closeNextActionLinkDialog(); });
        nextActionLinkUrl?.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); addNextActionLink(); } });
        nextActionSubjectMacro?.addEventListener('change', event => {
            const macro = event.target.value;
            if (macro && nextActionSubject) {
                insertTextAtCaret(nextActionSubject, macro);
                applyNextActionControl(nextActionSubject, true);
            }
            event.target.value = '';
        });
        nextActionBodyMacro?.addEventListener('change', event => {
            const macro = event.target.value;
            const action = activeNextAction();
            if (macro && action) {
                applyNextActionMessageMutation('Message Body', () => {
                    if (!action.isHtml) insertTextAtCaret(nextActionTextEditor, macro);
                    else if (nextActionHtmlSourceMode) insertTextAtCaret(nextActionHtmlSource, macro);
                    else insertNextActionHtmlMacro(macro);
                });
            }
            event.target.value = '';
        });
        nextActionTemplateButton?.addEventListener('click', event => {
            event.stopPropagation();
            if (nextActionTemplateMenu) nextActionTemplateMenu.hidden = !nextActionTemplateMenu.hidden;
        });
        destinationSmtpRecipientMacro?.addEventListener('change', event => {
            const macro = event.target.value;
            const input = dialog.querySelector('[data-destination-field="addressTo"]');
            if (macro && input) insertTextAtCaret(input, macro);
            if (input && macro) input.dispatchEvent(new Event('change', { bubbles: true }));
            event.target.value = '';
        });
        destinationSmtpSubjectMacro?.addEventListener('change', event => {
            const macro = event.target.value;
            const input = dialog.querySelector('[data-destination-field="subject"]');
            if (macro && input) insertTextAtCaret(input, macro);
            if (input && macro) input.dispatchEvent(new Event('change', { bubbles: true }));
            event.target.value = '';
        });
        destinationSmtpBodyMacro?.addEventListener('change', event => {
            const macro = event.target.value;
            const destination = activeSmtpDestination();
            if (macro && destination) {
                applyDestinationSmtpMessageMutation('Message', () => {
                    if (!destination.isHtml) insertTextAtCaret(destinationSmtpTextEditor, macro);
                    else if (destinationSmtpHtmlSourceMode) insertTextAtCaret(destinationSmtpHtmlSource, macro);
                    else insertDestinationSmtpHtmlMacro(macro);
                });
            }
            event.target.value = '';
        });
        destinationSmtpTemplateButton?.addEventListener('click', event => {
            event.stopPropagation();
            if (destinationSmtpTemplateMenu) destinationSmtpTemplateMenu.hidden = !destinationSmtpTemplateMenu.hidden;
        });
        destinationSmtpSaveTemplate?.addEventListener('click', () => saveEditorNotificationTemplate('smtp'));

        autoSaveToggle?.addEventListener('change', async () => {
            const requested = Boolean(autoSaveToggle.checked);
            const url = endpoint('traditionalTaskEditorPreferencesUrl');
            if (!url) { autoSaveToggle.checked = autoSave; setFooterStatus('Unable to save the Auto Save preference.', 'error'); return; }
            try {
                const result = await fetchJson(url, { method: 'POST', body: JSON.stringify({ autoSave: requested }) });
                autoSave = Boolean(result?.autoSave);
                autoSaveToggle.checked = autoSave;
                footerSaveError = '';
                updatePrimarySaveButton();
                updateDirtyState();
            } catch (preferenceError) {
                autoSaveToggle.checked = autoSave;
                setFooterStatus(preferenceError?.message || 'Unable to save the Auto Save preference.', 'error');
            }
        });

        reloadButton?.addEventListener('click', reloadAfterUncertainSave);
        overrideButton?.addEventListener('click', overrideCheckout);

        saveButton?.addEventListener('click', async () => {
            if (autoSave && !manualRecoverySave) {
                const saved = await flushPendingSave();
                if (saved) { await closeDialogWithCheckout(); }
                return;
            }
            await saveEditor();
        });

        document.addEventListener('click', event => {
            if (!dialog.open) return;
            if (actionsMenu && !actionsMenu.hidden && !event.target.closest('[data-traditional-task-editor-actions-menu]') && !event.target.closest('[data-traditional-task-editor-actions]')) closeActionsMenu();
            if (addStepMenu && !addStepMenu.hidden && !event.target.closest('[data-traditional-task-editor-add-step-menu]') && !event.target.closest('[data-traditional-task-editor-add-step]')) closeAddStepMenu();
            if (addNextActionMenu && !addNextActionMenu.hidden && !event.target.closest('[data-traditional-task-editor-add-next-action-menu]') && !event.target.closest('[data-traditional-task-editor-add-next-action]')) closeAddNextActionMenu();
            if (stepActionsMenu && !stepActionsMenu.hidden && !event.target.closest('[data-traditional-task-editor-step-actions-menu]') && !event.target.closest('[data-traditional-task-editor-step-actions]')) closeStepActionsMenu();
            if (scheduleActionsMenu && !scheduleActionsMenu.hidden && !event.target.closest('[data-traditional-task-editor-schedule-actions-menu]') && !event.target.closest('[data-traditional-task-editor-schedule-actions]')) closeScheduleActionsMenu();
            if (nextActionActionsMenu && !nextActionActionsMenu.hidden && !event.target.closest('[data-traditional-task-editor-next-action-actions-menu]') && !event.target.closest('[data-traditional-task-editor-next-action-actions]')) closeNextActionActionsMenu();
            if (nextActionTemplateMenu && !nextActionTemplateMenu.hidden && !event.target.closest('[data-traditional-task-editor-next-action-template-menu]') && !event.target.closest('[data-traditional-task-editor-next-action-template-button]')) nextActionTemplateMenu.hidden = true;
            if (destinationSmtpTemplateMenu && !destinationSmtpTemplateMenu.hidden && !event.target.closest('[data-traditional-task-editor-destination-smtp-template-menu]') && !event.target.closest('[data-traditional-task-editor-destination-smtp-template-button]')) destinationSmtpTemplateMenu.hidden = true;
        });

        advancedEditor = window.MauiAdvancedTaskEditor?.create({ dialog, enabled: isAdvancedTask,
            canEdit: () => canAlterTask && !saving, readOnly: () => !canAlterTask,
            remember: (flow,selection) => { lastUndo={kind:'advanced-flow',label:'Advanced Flow',flow,selection,steps:deepClone(taskSteps),actions:deepClone(nextActions)};updateUndoState(); }, steps: () => taskSteps, actions: () => nextActions,
            workspace: setWorkspace, selectStep, selectAction: selectNextAction, summary: stepRailDetail, actionSummary: nextActionSummaryText,
            formActions: key => {
                const step=taskSteps.find(s=>s.key===key);if(!step)return[];
                configureStepActions(step);
                return [...stepActionButtons].filter(button=>!button.hidden).map(button=>({id:button.dataset.traditionalTaskEditorStepAction,label:button.textContent,disabled:button.disabled,run:()=>handleStepAction(button.dataset.traditionalTaskEditorStepAction)}));
            },
            changed: () => { if (!saveOutcomeUncertain) footerSaveError = ''; updateDirtyState(); }, error: message => setFooterStatus(message, 'error'),
            addForm: kind => { if (kind === 'Source') addSource(); else if (kind === 'Process') addProcess(); else if (kind === 'Destination') addDestination(); else addNextAction(kind === 'Email' ? 'Email' : 'Task'); },
            removeForms: keys => { taskSteps = taskSteps.filter(s => !keys.includes(s.key)); nextActions = nextActions.filter(a => !keys.includes(a.key)); normalizeStepIndexes(); normalizeNextActionIndexes(); },
            cloneForm: (key, newKey) => {
                const step = taskSteps.find(s => s.key === key); const action = nextActions.find(a => a.key === key);
                if (step) { const value=deepClone(step); value.key=newKey; value.id=''; for (const name of ['source','process','destination']) if(value[name]) value[name].id=''; taskSteps.push(value); normalizeStepIndexes(); seedSourceStableValues(value); seedProcessStableValues(value); seedDestinationStableValues(value); return value; }
                if (action) { const value=deepClone(action); value.key=newKey; value.id=''; nextActions.push(value); normalizeNextActionIndexes(); seedNextActionStableValues(value); return value; }
                return null;
            }
        });
        window.MauiModalUI?.register(dialog, { error: message => setFooterStatus(message, 'error'), clear: () => updateDirtyState(), footerStatus: dirtyState });
        window.MauiModalUI?.registerContext('task-editor', dialog, {
            capture: () => ({ id: activeTaskId, name: activeTaskName, type: activeTaskType, selection: captureEditorSelection() }),
            prepare: () => flushPendingSave(),
            restore: async state => { if (await open(state.id, state.name, state.type)) restoreEditorSelection(state.selection); }
        });
        return { open, isTraditionalTaskType, supportsTaskType, getStepPatches: () => deepClone(buildMoveItStepPatches()) };
    }

    window.MauiTaskEditor = { initialize };
    window.MauiTraditionalTaskEditor = window.MauiTaskEditor;
})();

