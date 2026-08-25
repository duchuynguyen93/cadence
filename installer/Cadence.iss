; Bộ cài Cadence.
;
; Cài theo từng người dùng, không cần quyền quản trị. Một trình phát nhạc không
; có lý do gì ghi ra ngoài hồ sơ người dùng, và bắt nâng quyền mỗi lần cài là
; đúng loại phiền toái khiến người ta quay lại dùng app cũ.
;
; Build:  ISCC.exe installer\Cadence.iss

#define AppName "Cadence"
#define AppVersion "0.1.0"
#define AppPublisher "Cadence"
#define AppExeName "Cadence.exe"
#define SourceDir "..\dist"

[Setup]
AppId={{7F3C21D8-6B4E-4A9C-8E15-3D82A0F71C64}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=Cadence-{#AppVersion}-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Cadence.App\Assets\Cadence.ico
UninstallDisplayIcon={app}\{#AppExeName}

; Cài cho người dùng hiện tại, không hiện hộp thoại UAC.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; BASS và Avalonia đều không chạy dưới Windows 10 1809.
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo lối tắt ngoài desktop"; GroupDescription: "Lối tắt:"
Name: "associate"; Description: "Mở file nhạc bằng {#AppName}"; GroupDescription: "Liên kết file:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; App Paths: gõ "cadence" ở hộp Run là chạy được, không cần sửa PATH.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; \
    ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName}"; Flags: uninsdeletekey

; Khai báo năng lực thay vì ghi thẳng khoá cho từng đuôi file. Nhờ vậy Windows
; đưa Cadence vào "Open with" và mục Ứng dụng mặc định, chứ không âm thầm giành
; lấy liên kết file của người dùng.
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities"; \
    ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities"; \
    ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Trình phát nhạc local"
Root: HKCU; Subkey: "Software\RegisteredApplications"; \
    ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\{#AppName}\Capabilities"; \
    Flags: uninsdeletevalue

Root: HKCU; Subkey: "Software\Classes\{#AppName}.Audio"; \
    ValueType: string; ValueName: ""; ValueData: "File nhạc"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#AppName}.Audio\DefaultIcon"; \
    ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKCU; Subkey: "Software\Classes\{#AppName}.Audio\shell\open\command"; \
    ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

; CHỈ đăng ký những đuôi file thật sự phát được. .opus, .ape, .wv, .dsf cần
; plugin BASS chưa bundle — scanner có index nhưng phát sẽ lỗi, mà một đuôi file
; đã đăng ký rồi không mở được thì Windows đổ lỗi cho app. Danh sách này phải đi
; cùng phần "Đã chạy và đã kiểm chứng" trong README.
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp3";  ValueData: "{#AppName}.Audio"; Tasks: associate
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".flac"; ValueData: "{#AppName}.Audio"; Tasks: associate
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wav";  ValueData: "{#AppName}.Audio"; Tasks: associate
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogg";  ValueData: "{#AppName}.Audio"; Tasks: associate
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m4a";  ValueData: "{#AppName}.Audio"; Tasks: associate
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aac";  ValueData: "{#AppName}.Audio"; Tasks: associate

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Chạy {#AppName}"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Thư viện đã index, ảnh bìa đã cache và settings đều nằm ở đây. Xoá lúc gỡ cài
; để lần cài lại không thừa hưởng một database schema cũ.
Type: filesandordirs; Name: "{localappdata}\{#AppName}"
