using Alba;
using Application.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Tests;

public class UnitTest1 : IAsyncLifetime
{
    private readonly TestcontainerDatabase Testcontainer = new TestcontainersBuilder<PostgreSqlTestcontainer>()
        .WithDatabase(new PostgreSqlTestcontainerConfiguration
        {
            Database = "postgres",
            Username = "postgres",
            Password = "Password12!",
        })
        .Build();

    public async Task InitializeAsync()
    {
        await Testcontainer.StartAsync();
    }

    public Task DisposeAsync()
    {
        return Testcontainer.DisposeAsync().AsTask();
    }

    [Fact]
    public async void Test1()
    {
        await using var host = await AlbaHost.For<Program>(x => { x.ConfigureServices((context, services) => { }); });

        var result = await host.GetAsJson<List<WeatherForecast>>("/WeatherForecast");

        result.Should().HaveCount(5);
    }

    [Fact]
    public async void Test2()
    {
        await using var host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseSetting("ConnectionStrings:TodoContext", Testcontainer.ConnectionString);
            builder.ConfigureServices((_, services) =>
                services.BuildServiceProvider().GetService<TodoContext>()?.Database.Migrate());
        });

        await host.Scenario(_ =>
        {
            _.StatusCodeShouldBe(201);
            _.Post
                .Json(new TodoItem
                {
                    Id = 1L,
                    Name = "TestName",
                    IsComplete = false
                })
                .ToUrl("/api/TodoItems");
        });

        var result = await host.GetAsJson<TodoItem>("/api/TodoItems/1");

        result?.Name.Should().Be("TestName");
    }
}