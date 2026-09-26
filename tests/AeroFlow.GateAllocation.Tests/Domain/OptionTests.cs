using AeroFlow.GateAllocation.Domain;
using Xunit;

namespace AeroFlow.GateAllocation.Tests.Domain;

public sealed class OptionTests
{
    [Fact]
    public void Map_and_bind_run_on_some()
    {
        var result = Option.Some(20).Map(x => x + 1).Bind(x => Option.Some(x * 2));

        Assert.Equal(Option.Some(42), result);
    }

    [Fact]
    public void Map_and_bind_skip_none()
    {
        var called = false;

        var result = Option.None<int>().Map(x => { called = true; return x; }).Bind(Option.Some);

        Assert.Equal(Option.None<int>(), result);
        Assert.False(called);
    }

    [Fact]
    public void Bind_can_turn_some_into_none()
    {
        Assert.Equal(Option.None<int>(), Option.Some(1).Bind(_ => Option.None<int>()));
    }

    [Fact]
    public void Match_picks_the_matching_branch()
    {
        Assert.Equal("some 3", Option.Some(3).Match(x => $"some {x}", () => "none"));
        Assert.Equal("none", Option.None<int>().Match(x => $"some {x}", () => "none"));
    }

    [Fact]
    public void ToResult_turns_absence_into_the_given_error()
    {
        var error = new GateNotFound(TestPlans.Id("9"));

        Assert.Equal(new Result<int>.Ok(5), Option.Some(5).ToResult(() => error));
        Assert.Equal(new Result<int>.Failure(error), Option.None<int>().ToResult(() => error));
    }

    [Fact]
    public void FirstOrNone_and_Find_say_maybe_in_the_type()
    {
        Assert.Equal(Option.Some(4), new[] { 1, 4, 6 }.FirstOrNone(x => x % 2 == 0));
        Assert.Equal(Option.None<int>(), Array.Empty<int>().FirstOrNone());

        IReadOnlyDictionary<string, int> dict = new Dictionary<string, int> { ["a"] = 1 };
        Assert.Equal(Option.Some(1), dict.Find("a"));
        Assert.Equal(Option.None<int>(), dict.Find("b"));
    }
}
