namespace Daycord.Core.Application.Contracts;

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}