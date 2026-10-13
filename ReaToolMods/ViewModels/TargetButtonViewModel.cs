using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaToolMods.Models;

namespace ReaToolMods.ViewModels;

/// <summary>
/// 타겟 선택 버튼 하나. ViewLocator 대상이 되지 않도록 ViewModelBase가 아닌 ObservableObject를 상속합니다.
/// </summary>
public partial class TargetButtonViewModel : ObservableObject
{
    public TargetProfile Profile { get; }
    public IRelayCommand SelectCommand { get; }

    public string Tooltip =>
        $"허용 확장자: {string.Join(", ", Profile.Extensions)}\n작업 폴더 접미사: {Profile.FolderSuffix}";

    /// <summary>현재 선택된 타겟</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>자동 판별 결과와 일치하는 타겟 (선택과 다르면 버튼에 표시)</summary>
    [ObservableProperty]
    public partial bool IsDetected { get; set; }

    public TargetButtonViewModel(TargetProfile profile, Action<TargetProfile> onSelect)
    {
        Profile = profile;
        SelectCommand = new RelayCommand(() => onSelect(profile));
    }
}
