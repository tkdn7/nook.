using Nook.App.ViewModels;

namespace Nook.App.Services;

/// <summary>Prepared windows keep the current desktop alive until commit succeeds.</summary>
public interface IPresetWindowChange : IDisposable
{
    void Commit();
    void Complete();
}
