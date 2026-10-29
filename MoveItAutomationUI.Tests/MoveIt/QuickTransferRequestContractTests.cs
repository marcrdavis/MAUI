// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class QuickTransferRequestContractTests
{
    [Fact]
    public void ConfirmationEmail_DefaultsDisabled()
    {
        var request = new QuickTransferRequest();
        Assert.False(request.SendTransferConfirmationEmail);
        Assert.Equal(string.Empty, request.ConfirmationEmailRecipients);
    }

    [Fact]
    public void NormalizeRequest_PreservesRecipientsOnlyWhenEnabled()
    {
        var request = new QuickTransferRequest
        {
            SendTransferConfirmationEmail = true,
            ConfirmationEmailRecipients = " user@example.com; team@example.com "
        };
        var enabled = QuickTransferService.NormalizeRequest(request);
        Assert.True(enabled.SendTransferConfirmationEmail);
        Assert.Equal("user@example.com; team@example.com", enabled.ConfirmationEmailRecipients);

        request.SendTransferConfirmationEmail = false;
        var disabled = QuickTransferService.NormalizeRequest(request);
        Assert.False(disabled.SendTransferConfirmationEmail);
        Assert.Equal(string.Empty, disabled.ConfirmationEmailRecipients);
    }

    [Fact]
    public void ResourceGroup_IsNotAQuickTransferRequestOption()
    {
        Assert.Null(typeof(QuickTransferRequest).GetProperty("ResourceGroup"));
    }

    [Fact]
    public void OverwriteExistingFile_DefaultsEnabled()
    {
        var request = new QuickTransferRequest();

        Assert.True(request.OverwriteExistingFile);
    }

    [Fact]
    public void OverwriteExistingFile_CanBeDisabled()
    {
        var request = new QuickTransferRequest { OverwriteExistingFile = false };

        Assert.False(request.OverwriteExistingFile);
    }

    [Fact]
    public void DeleteSourceFileAfterSuccess_DefaultsDisabled()
    {
        var request = new QuickTransferRequest();

        Assert.False(request.DeleteSourceFileAfterSuccess);
    }

    [Fact]
    public void DeleteSourceFileAfterSuccess_CanBeEnabled()
    {
        var request = new QuickTransferRequest { DeleteSourceFileAfterSuccess = true };

        Assert.True(request.DeleteSourceFileAfterSuccess);
    }

    [Fact]
    public void NormalizeRequest_ClearsPgpKeyWhenEncryptionIsNotSelected()
    {
        var request = new QuickTransferRequest
        {
            ProcessingMode = "decrypt",
            PgpKeyId = " 123 "
        };

        var normalized = QuickTransferService.NormalizeRequest(request);

        Assert.Equal("decrypt", normalized.ProcessingMode);
        Assert.Equal(string.Empty, normalized.PgpKeyId);
    }

    [Fact]
    public void NormalizeRequest_ClearsTemporaryOnlyFieldsForPersistentTask()
    {
        var request = new QuickTransferRequest
        {
            CreateTraditionalTask = true,
            TaskName = "  TEST_TASK  ",
            DeleteTemporaryTaskAfterSuccess = true
        };

        var normalized = QuickTransferService.NormalizeRequest(request);

        Assert.True(normalized.CreateTraditionalTask);
        Assert.Equal("TEST_TASK", normalized.TaskName);
        Assert.False(normalized.DeleteTemporaryTaskAfterSuccess);
    }

    [Fact]
    public void NormalizeRequest_PreservesPgpKeyForEncryption()
    {
        var request = new QuickTransferRequest
        {
            ProcessingMode = " encrypt ",
            PgpKeyId = " 123 "
        };

        var normalized = QuickTransferService.NormalizeRequest(request);

        Assert.Equal("encrypt", normalized.ProcessingMode);
        Assert.Equal("123", normalized.PgpKeyId);
    }

    [Fact]
    public void NormalizeRequest_ClearsTaskNameForTemporaryTask()
    {
        var request = new QuickTransferRequest
        {
            CreateTraditionalTask = false,
            TaskName = "STALE_HIDDEN_NAME"
        };

        var normalized = QuickTransferService.NormalizeRequest(request);

        Assert.False(normalized.CreateTraditionalTask);
        Assert.Equal(string.Empty, normalized.TaskName);
    }

}
