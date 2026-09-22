using Microsoft.AspNetCore.HttpOverrides;
using Tichu.Hub;
using Tichu.Models;
using Tichu.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<GameService>();
builder.Services.AddSingleton<TichuRuleEngine>();
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<TimerService>();
builder.Services.AddSingleton<RoomBroadcaster>();
builder.Services.AddSingleton<TurnTimerCoordinator>();

// 렌더/레일웨이/플라이 등 호스팅 플랫폼은 PORT 환경변수로 리슨 포트를 지정한다.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var app = builder.Build();

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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<TichuHub>("/tichuHub");

app.Run();
