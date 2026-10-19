WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

// Gli endpoint /ws e /health vengono aggiunti nel punto 1A.2.
app.Run();
