using MovieDB.Common;
using MovieDB.Infrastructure;
using Shouldly;

namespace MovieDB.UnitTests.Infrastructure;

public sealed class DirectorsKeyTests
{
    private static DirectorId[] Ids(params int[] values) => [.. values.Select(DirectorId.From)];

    [Fact]
    public void The_ids_are_sorted_numerically_not_lexically()
    {
        DirectorsKey.Of(Ids(10, 2)).ShouldBe("2,10");
    }

    [Fact]
    public void The_order_of_the_ids_does_not_matter()
    {
        DirectorsKey.Of(Ids(3, 1, 2)).ShouldBe(DirectorsKey.Of(Ids(2, 3, 1)));
    }

    [Fact]
    public void A_single_id_has_no_separator()
    {
        DirectorsKey.Of(Ids(57)).ShouldBe("57");
    }

    [Fact]
    public void A_different_set_gives_a_different_key()
    {
        DirectorsKey.Of(Ids(1, 2)).ShouldNotBe(DirectorsKey.Of(Ids(1, 2, 3)));
        DirectorsKey.Of(Ids(1, 2)).ShouldNotBe(DirectorsKey.Of(Ids(1)));
    }

    [Fact]
    public void Five_of_the_largest_ids_fit_the_column()
    {
        DirectorsKey
            .Of(Ids(int.MaxValue, int.MaxValue - 1, int.MaxValue - 2, int.MaxValue - 3, int.MaxValue - 4))
            .Length.ShouldBeLessThanOrEqualTo(DirectorsKey.MaxLength);
    }
}
