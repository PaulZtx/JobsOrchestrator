using System.Text.Json.Serialization;
using JobsOperator.Protos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

var workerAddress = builder.Configuration["Worker:Address"]
    ?? throw new InvalidOperationException("Worker address is not configured");

builder.Services.AddGrpcClient<JobService.JobServiceClient>(options =>
{
    options.Address = new Uri(workerAddress);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Jobs API v1");
    });
}

app.MapControllers();

app.Run();

