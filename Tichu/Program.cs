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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<TichuHub>("/tichuHub");

app.Run();
