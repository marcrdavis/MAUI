using System.Reflection;
using System.Xml.Linq;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostCloneTests
{
    [Theory]
    [InlineData("Share")][InlineData("siLock")][InlineData("FTP")][InlineData("SSHFTP")]
    [InlineData("POP3")][InlineData("SMTP")][InlineData("S3")][InlineData("AzureBlob")]
    [InlineData("SharePoint")][InlineData("GoogleStorage")]
    public void NativeCloneChangesOnlyRootIdentityAndName(string type)
    {
        var source = HostCloneDefinition.Read($"<{type} ID='42' Name='Original' DefPassword='SERVER_CIPHER' DefClientKeyID='7' ID2='9' NativeOnly='keep'><Desc>Source description</Desc><Group>Operations</Group><Advanced value='keep'/></{type}>", "42");
        var cloned = XElement.Parse(HostCloneDefinition.Compose(source,"900","Clone"));
        Assert.Equal("900",(string?)cloned.Attribute("ID")); Assert.Equal("Clone",(string?)cloned.Attribute("Name"));
        Assert.Equal("42",(string?)source.Attribute("ID")); Assert.Equal("Original",(string?)source.Attribute("Name"));
        cloned.SetAttributeValue("ID","42"); cloned.SetAttributeValue("Name","Original");
        Assert.True(XNode.DeepEquals(source,cloned));
    }
    [Theory]
    [InlineData("<FileSystem ID='0' />", "0")]
    [InlineData("<FTP ID='99' />", "42")]
    [InlineData("<FTP />", "42")]
    public void InvalidSourcesAreRejected(string xml,string id)
        => Assert.Throws<InvalidOperationException>(()=>HostCloneDefinition.Read(xml,id));
    [Fact]
    public void CloneRequiresADifferentPersistentId()
        => Assert.Throws<InvalidOperationException>(()=>HostCloneDefinition.Compose(XElement.Parse("<FTP ID='42'/>"),"42","Clone"));
    [Fact]
    public async Task CoordinatorUsesOneExistingNativeCreationPathAndDoesNotMutateTheSource()
    {
        var writes=0; var source="<FTP ID='42' Name='Original' DefPassword='SERVER_CIPHER'/>";
        var com=Stub<IMoveItComClient>((method,args)=>method.Name switch {
            nameof(IMoveItComClient.GetHostConfigurationAsync)=>Task.FromResult(new MoveItHostConfiguration("42","Original","FTP",source)),
            nameof(IMoveItComClient.CreateHostFromConfigurationAsync)=>Create(args),
            _=>throw new NotSupportedException(method.Name)
        });
        Task<MoveItCreatedHost> Create(object?[] args) {
            writes++; Assert.Equal("Clone",args[0]); Assert.Equal(source,args[1]);
            return Task.FromResult(new MoveItCreatedHost("900","Clone","FTP","FTP"));
        }
        var crud=new HostCrudCoordinator(Stub<IMoveItRestClient>((m,_)=>throw new NotSupportedException(m.Name)),com,null!);
        var result=await crud.CloneAsync("42","Clone");
        Assert.Equal("900",result.Id); Assert.Equal(1,writes);
    }
    private static T Stub<T>(Func<MethodInfo,object?[],object?> handler) where T:class {
        var value=DispatchProxy.Create<T,Proxy>();((Proxy)(object)value).Handler=handler;return value;
    }
    public class Proxy:DispatchProxy {
        public Func<MethodInfo,object?[],object?> Handler {get;set;}=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>Handler(method!,args??Array.Empty<object?>());
    }
}
