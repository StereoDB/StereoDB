using LoadTests.Redis.Scenarios;
using NBomber.CSharp;

namespace LoadTests.Redis;

public class RedisLoadTest
{
    public static void Run(string[] args)
    {
        NBomberRunner.RegisterScenarios(
            new RedisInitScenario().Create(),
            new RedisReadScenario().Create(),
            new RedisWriteScenario().Create()
        )
        //.LoadConfig("./Redis/Configs/swarm-nbomber-config.json")
        .Run(args);
    }
}