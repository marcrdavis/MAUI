// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Verifies the captured MOVEit /api/v1/failoverstatus payload contract.

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class FailoverStatusContractTests
{
    [Fact]
    public void ParseFailoverStatus_ConfiguredPayload_PreservesNodeRolesHealthAndStatusFields()
    {
        var root = JsonNode.Parse("""
        {
          "ResilEnabled": true,
          "ResilStatus": "All OK.  Logged into remote Central DC1WPVMAUAPP42.arifleet.com OK with SSL",
          "ResilStatusStamp": "2026-10-10 14:40:47",
          "ResilStatusSec": "Replicated config\nThe other node is not logged into this node.",
          "ResilStatusStampSec": "2026-10-10 14:40:45",
          "ThisNode": 1,
          "Nodes": {
            "Node": [
              {
                "NodeNum": 1,
                "Hostname": "DC1WPVMAUAPP41",
                "ResilRole": "primary",
                "NodeHasError": false
              },
              {
                "NodeNum": 2,
                "Hostname": "DC1WPVMAUAPP42.arifleet.com",
                "ResilRole": "secondary",
                "NodeHasError": true
              }
            ]
          }
        }
        """)!.AsObject();

        var status = MoveItRestClient.ParseFailoverStatus(root);

        Assert.True(status.ResilEnabled);
        Assert.Equal(1, status.ThisNode);
        Assert.Equal("All OK.  Logged into remote Central DC1WPVMAUAPP42.arifleet.com OK with SSL", status.PrimaryStatus);
        Assert.Equal("2026-10-10 14:40:47", status.PrimaryStatusStamp);
        Assert.Equal("Replicated config\nThe other node is not logged into this node.", status.SecondaryStatus);
        Assert.Equal("2026-10-10 14:40:45", status.SecondaryStatusStamp);
        Assert.Collection(status.Nodes,
            node => Assert.Equal(1, node.NodeNum),
            node => Assert.Equal(2, node.NodeNum));

        var primary = status.Nodes[0];
        Assert.Equal(1, primary.NodeNum);
        Assert.Equal("DC1WPVMAUAPP41", primary.Hostname);
        Assert.Equal("primary", primary.ResilRole);
        Assert.False(primary.NodeHasError);

        var secondary = status.Nodes[1];
        Assert.Equal(2, secondary.NodeNum);
        Assert.Equal("DC1WPVMAUAPP42.arifleet.com", secondary.Hostname);
        Assert.Equal("secondary", secondary.ResilRole);
        Assert.True(secondary.NodeHasError);
    }


    [Fact]
    public void FailoverStatus_RequiresMauiAdminPolicy()
    {
        var method = typeof(MoveItServerController).GetMethod(nameof(MoveItServerController.FailoverStatus));
        Assert.NotNull(method);

        var policies = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Select(attribute => attribute.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .ToArray();

        Assert.Contains(MauiSecurityPolicies.Admin, policies);
    }

    [Fact]
    public void ParseFailoverStatus_DisabledPayload_HasNoInventedNodesOrStatus()
    {
        var root = JsonNode.Parse("""
        {
          "ResilEnabled": false,
          "NSQLRepl": 0,
          "NSQLPending": 0,
          "NMiscRepl": 0,
          "NMiscPending": 0,
          "ThisNode": 0
        }
        """)!.AsObject();

        var status = MoveItRestClient.ParseFailoverStatus(root);

        Assert.False(status.ResilEnabled);
        Assert.Equal(0, status.ThisNode);
        Assert.Empty(status.Nodes);
        Assert.Equal(string.Empty, status.PrimaryStatus);
        Assert.Equal(string.Empty, status.SecondaryStatus);
    }
}
