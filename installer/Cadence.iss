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
#define ProgId "Cadence.Audio"
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
ChangesAssociations=yes
; Bảo Inno phát SHCNE_ASSOCCHANGED sau khi cài. Thiếu dòng này thì Explorer
; vẫn dùng bộ nhớ đệm liên kết cũ cho tới lần đăng nhập sau — cài xong thử
; ngay sẽ thấy "không có tác dụng gì" dù registry đã đúng.
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

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; App Paths: gõ "cadence" ở hộp Run là chạy được, không cần sửa PATH.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; \
    ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName}"; Flags: uninsdeletekey

; Khai báo năng lực: đây là thứ đưa app vào mục Ứng dụng mặc định trong Cài đặt.
; Một mình nó KHÔNG đưa app vào menu "Open with" — bản đầu chỉ có mỗi cái này nên
; phần liên kết file trông như chẳng làm gì. Xem ghi chú ba cơ chế phía dưới.
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities"; \
    ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities"; \
    ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Trình phát nhạc local"
Root: HKCU; Subkey: "Software\RegisteredApplications"; \
    ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\{#AppName}\Capabilities"; \
    Flags: uninsdeletevalue

Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; \
    ValueType: string; ValueName: ""; ValueData: "File nhạc"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; \
    ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; \
    ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

; Ba cơ chế khác nhau, mỗi cái phục vụ một chỗ trong Windows. Thiếu bất kỳ
; cái nào là app biến mất khỏi đúng chỗ đó, nên đừng gộp.
;
;   Capabilities\FileAssociations  -> mục Ứng dụng mặc định trong Cài đặt
;   <đuôi>\OpenWithProgIds         -> menu "Open with" khi chuột phải
;   Classes\Applications\<exe>     -> hộp thoại "Choose another app"
;
; Đăng ký KHÔNG điều kiện, không còn tick chọn. Xuất hiện trong "Open with"
; là vô hại và đúng thứ người dùng mong đợi sau khi cố ý cài app; còn việc
; ĐẶT LÀM MẶC ĐỊNH thì từ Windows 10 bộ cài không được phép tự làm nữa —
; xem mục [Run] ở cuối file.
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp3"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".flac"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wav"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogg"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m4a"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aac"; ValueData: "{#ProgId}"

Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgIds"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgIds"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgIds"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgIds"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgIds"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.aac\OpenWithProgIds"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue

Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mp3"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".flac"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".wav"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ogg"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".m4a"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".aac"; ValueData: ""

[Run]

; Windows 10 trở đi không cho bộ cài tự giành liên kết mặc định. Thứ duy nhất
; làm được là mở đúng trang đó ra để người dùng tự chọn — trung thực hơn hẳn
; một cái tick hứa điều không thể xảy ra.
Filename: "{app}\{#AppExeName}"; Description: "Chạy {#AppName}"; \
    Flags: nowait postinstall skipifsilent
Filename: "ms-settings:defaultapps"; Description: "Chọn {#AppName} làm trình phát mặc định (mở Cài đặt Windows)"; \
    Flags: postinstall shellexec nowait skipifsilent unchecked

[UninstallDelete]
; Thư viện đã index, ảnh bìa đã cache và settings đều nằm ở đây. Xoá lúc gỡ cài
; để lần cài lại không thừa hưởng một database schema cũ.
Type: filesandordirs; Name: "{localappdata}\{#AppName}"
