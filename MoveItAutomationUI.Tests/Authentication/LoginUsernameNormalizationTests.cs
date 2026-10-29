using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Upgrade;

namespace MoveItAutomationUI.Tests.Authentication;

public sealed class LoginUsernameNormalizationTests
{
    [Theory]
    [InlineData(" DOMAIN/Username ", "DOMAIN\\Username")]
    [InlineData("domain\\USERNAME", "domain\\USERNAME")]
    [InlineData(" User@Example.com ", "User@Example.com")]
    public async Task PasswordGrantAndSessionUseNormalizedUsernameButUnchangedPassword(string input, string expected)
    {
        using var handler = new TokenHandler();
        using var factory = new ClientFactory(handler);
        var rest = Client(factory);
        var session = await rest.AuthenticateAsync("test", input, " P/a\\ss word ");
        Assert.Equal(expected, handler.Username);
        Assert.Equal(expected, session.Username);
        Assert.Equal(" P/a\\ss word ", handler.Password);
        Assert.Equal(handler.Password, session.Password);
    }

    [Fact]
    public async Task LoginPostWithoutJavascriptNormalizesTheUsernameOnAuthenticationFailure()
    {
        using var handler = new TokenHandler { Fail = true };
        using var factory = new ClientFactory(handler);
        var controller = new AccountController(Catalog(), Client(factory), null!, null!, new ValidAntiforgery(),
            null!, null!, new MauiUpgradeState(), NullLogger<AccountController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = await controller.Login(" DOMAIN/Username ", "password", "test", CancellationToken.None);
        Assert.IsType<ViewResult>(result);
        Assert.Equal("DOMAIN\\Username", handler.Username);
        Assert.Equal("DOMAIN\\Username", (string)controller.ViewBag.Username);
        Assert.False(controller.ModelState.IsValid);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["MoveIt:Servers:0:Key"] = "test", ["MoveIt:Servers:0:WebAdminUrl"] = "https://automation.example.invalid"
    }).Build();
    private static MoveItServerCatalog Catalog() => new(Configuration());
    private static MoveItRestClient Client(IHttpClientFactory factory) => new(factory, null!,
        ConfigurationStartupState.Capture(Configuration()), Catalog(), NullLogger<MoveItRestClient>.Instance);

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly List<HttpClient> _clients = [];
        public HttpClient CreateClient(string name)
        { var client = new HttpClient(handler, disposeHandler: false); _clients.Add(client); return client; }
        public void Dispose() { foreach (var client in _clients) client.Dispose(); }
    }
    private sealed class TokenHandler : HttpMessageHandler
    {
        public string? Username, Password;
        public bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/token", request.RequestUri!.AbsolutePath);
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            Username = form["username"].ToString(); Password = form["password"].ToString();
            return new HttpResponseMessage(Fail ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            { Content = new StringContent(Fail ? "Invalid credentials" : """{"access_token":"token","refresh_token":"refresh","expires_in":900}""") };
        }
    }
    private sealed class ValidAntiforgery : IAntiforgery
    {
        public Task ValidateRequestAsync(HttpContext context) => Task.CompletedTask;
        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(true);
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => throw new NotSupportedException();
        public AntiforgeryTokenSet GetTokens(HttpContext context) => throw new NotSupportedException();
        public void SetCookieTokenAndHeader(HttpContext context) => throw new NotSupportedException();
    }
}
