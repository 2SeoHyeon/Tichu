using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Tichu.Controllers;
using Tichu.Data;
using Tichu.Hub;
using Tichu.Models;
using Tichu.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddDbContextFactory<TichuDbContext>(options =>
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(databaseUrl))
    {
        options.UseNpgsql(ConvertDatabaseUrlToNpgsqlConnectionString(databaseUrl));
    }
    else
    {
        var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        var sqlitePath = Path.Combine(dataDir, "tichu.db");
        options.UseSqlite($"Data Source={sqlitePath}");
    }
});

builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<GameService>();
builder.Services.AddSingleton<TichuRuleEngine>();
builder.Services.AddSingleton<PlayerStatsService>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<TimerService>();
builder.Services.AddSingleton<RoomBroadcaster>();
builder.Services.AddSingleton<TurnTimerCoordinator>();

// 소셜 로그인 성공 시 계정을 연결하고 게임에서 쓰는 tichu_uid/tichu_nick 쿠키를 발급한다.
async Task OnExternalTicketReceived(TicketReceivedContext context)
{
    var provider = context.Scheme.Name;
    var principal = context.Principal!;
    var providerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    var nickname = principal.FindFirstValue("urn:tichu:nickname")
        ?? principal.FindFirstValue(ClaimTypes.Name)
        ?? principal.FindFirstValue(ClaimTypes.GivenName)
        ?? "플레이어";

    var accountService = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
    var (uid, finalNickname) = accountService.FindOrCreateAccount(provider, providerUserId, nickname);

    var cookieOptions = new CookieOptions
    {
        Expires = DateTimeOffset.UtcNow.AddDays(7),
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true
    };
    context.HttpContext.Response.Cookies.Append(HomeController.UidCookie, uid, cookieOptions);
    context.HttpContext.Response.Cookies.Append(HomeController.NickCookie, finalNickname, cookieOptions);

    context.HandleResponse();
    context.HttpContext.Response.Redirect("/Lobby");

    await Task.CompletedTask;
}

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
});

var googleClientId = builder.Configuration["Auth:Google:ClientId"];
var googleClientSecret = builder.Configuration["Auth:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.CallbackPath = "/signin-google";
        options.Events.OnTicketReceived = OnExternalTicketReceived;
    });
}

var kakaoClientId = builder.Configuration["Auth:Kakao:ClientId"];
if (!string.IsNullOrEmpty(kakaoClientId))
{
    authBuilder.AddOAuth("Kakao", "Kakao", options =>
    {
        options.ClientId = kakaoClientId;
        // 카카오는 "Client Secret" 기능을 켜지 않은 앱이면 실제로는 시크릿이 필요 없지만,
        // ASP.NET Core의 OAuthOptions.Validate()는 빈 문자열을 허용하지 않아 자리채움 값을 넣는다.
        options.ClientSecret = builder.Configuration["Auth:Kakao:ClientSecret"] is string s && !string.IsNullOrEmpty(s)
            ? s
            : "kakao-oauth-unused";
        options.CallbackPath = "/signin-kakao";
        options.AuthorizationEndpoint = "https://kauth.kakao.com/oauth/authorize";
        options.TokenEndpoint = "https://kauth.kakao.com/oauth/token";
        options.UserInformationEndpoint = "https://kapi.kakao.com/v2/user/me";
        options.SaveTokens = true;

        options.ClaimActions.MapCustomJson(ClaimTypes.NameIdentifier, json => json.GetProperty("id").GetRawText());
        options.ClaimActions.MapCustomJson("urn:tichu:nickname", json =>
        {
            try
            {
                return json.GetProperty("kakao_account").GetProperty("profile").GetProperty("nickname").GetString();
            }
            catch
            {
                return null;
            }
        });

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
                var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();
                using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                context.RunClaimActions(doc.RootElement);
            },
            OnTicketReceived = OnExternalTicketReceived
        };
    });
}

var naverClientId = builder.Configuration["Auth:Naver:ClientId"];
if (!string.IsNullOrEmpty(naverClientId))
{
    authBuilder.AddOAuth("Naver", "Naver", options =>
    {
        options.ClientId = naverClientId;
        options.ClientSecret = builder.Configuration["Auth:Naver:ClientSecret"] ?? "";
        options.CallbackPath = "/signin-naver";
        options.AuthorizationEndpoint = "https://nid.naver.com/oauth2.0/authorize";
        options.TokenEndpoint = "https://nid.naver.com/oauth2.0/token";
        options.UserInformationEndpoint = "https://openapi.naver.com/v1/nid/me";
        options.SaveTokens = true;

        options.ClaimActions.MapCustomJson(ClaimTypes.NameIdentifier, json => json.GetProperty("response").GetProperty("id").GetString());
        options.ClaimActions.MapCustomJson("urn:tichu:nickname", json =>
        {
            try
            {
                return json.GetProperty("response").GetProperty("nickname").GetString();
            }
            catch
            {
                return null;
            }
        });

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
                var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();
                using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                context.RunClaimActions(doc.RootElement);
            },
            OnTicketReceived = OnExternalTicketReceived
        };
    });
}

// 렌더/레일웨이/플라이 등 호스팅 플랫폼은 PORT 환경변수로 리슨 포트를 지정한다.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TichuDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.EnsureCreated();
}

// 렌더 등의 리버스 프록시 뒤에서 X-Forwarded-* 헤더로 원본 스킴/IP를 인식한다.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// TLS는 호스팅 플랫폼의 프록시가 종단 처리하므로 컨테이너 내부에서는 리다이렉트하지 않는다.
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<TichuHub>("/tichuHub");

app.Run();

static string ConvertDatabaseUrlToNpgsqlConnectionString(string databaseUrl)
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    var builder = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        Database = uri.AbsolutePath.TrimStart('/'),
        SslMode = Npgsql.SslMode.Require
    };
    return builder.ToString();
}
