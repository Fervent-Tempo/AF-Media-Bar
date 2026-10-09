; *** Inno Setup version 6.5.0+ Vietnamese messages ***
;
; Note: When translating this text, do not add periods (.) to the end of
; messages that didn't have them already, because on those messages Inno
; Setup adds the periods automatically (appending a period would result in
; two periods being displayed).

[LangOptions]
LanguageName=Tiếng Việt
LanguageID=$042A
LanguageCodePage=0
;DialogFontName=
;DialogFontSize=9
;DialogFontBaseScaleWidth=7
;DialogFontBaseScaleHeight=15
;WelcomeFontName=Segoe UI
;WelcomeFontSize=14

[Messages]

; *** Application titles
SetupAppTitle=Cài đặt
SetupWindowTitle=Cài đặt - %1
UninstallAppTitle=Gỡ cài đặt
UninstallAppFullTitle=Gỡ cài đặt %1

; *** Misc. common
InformationTitle=Thông tin
ConfirmTitle=Xác nhận
ErrorTitle=Lỗi

; *** SetupLdr messages
SetupLdrStartupMessage=Chương trình sẽ cài đặt %1. Bạn có muốn tiếp tục không?
LdrCannotCreateTemp=Không thể tạo tệp tạm thời. Đã hủy cài đặt
LdrCannotExecTemp=Không thể thực thi tệp trong thư mục tạm thời. Đã hủy cài đặt
HelpTextNote=

; *** Startup error messages
LastErrorMessage=%1.%n%nLỗi %2: %3
SetupFileMissing=Thiếu tệp %1 trong thư mục cài đặt. Vui lòng khắc phục sự cố hoặc tải lại chương trình.
SetupFileCorrupt=Tệp cài đặt bị hỏng. Vui lòng tải lại chương trình.
SetupFileCorruptOrWrongVer=Tệp cài đặt bị hỏng hoặc không tương thích với phiên bản trình cài đặt này. Vui lòng khắc phục sự cố hoặc tải lại chương trình.
InvalidParameter=Tham số dòng lệnh không hợp lệ:%n%n%1
SetupAlreadyRunning=Trình cài đặt đang chạy.
WindowsVersionNotSupported=Chương trình này không hỗ trợ phiên bản Windows đang chạy trên máy tính của bạn.
WindowsServicePackRequired=Chương trình này yêu cầu %1 Service Pack %2 hoặc mới hơn.
NotOnThisPlatform=Chương trình này không thể chạy trên %1.
OnlyOnThisPlatform=Chương trình này chỉ có thể chạy trên %1.
OnlyOnTheseArchitectures=Chương trình này chỉ có thể cài đặt trên các phiên bản Windows dành cho kiến trúc bộ xử lý sau:%n%n%1
WinVersionTooLowError=Chương trình này yêu cầu %1 phiên bản %2 hoặc mới hơn.
WinVersionTooHighError=Chương trình này không thể cài đặt trên %1 phiên bản %2 hoặc mới hơn.
AdminPrivilegesRequired=Bạn phải đăng nhập với quyền quản trị viên (Administrator) khi cài đặt chương trình này.
PowerUserPrivilegesRequired=Bạn phải đăng nhập với quyền quản trị viên hoặc thành viên nhóm Power Users khi cài đặt chương trình này.
SetupAppRunningError=Trình cài đặt phát hiện %1 hiện đang chạy.%n%nVui lòng đóng tất cả các cửa sổ của chương trình, sau đó bấm OK để tiếp tục hoặc Hủy để thoát.
UninstallAppRunningError=Trình gỡ cài đặt phát hiện %1 hiện đang chạy.%n%nVui lòng đóng tất cả các cửa sổ của chương trình, sau đó bấm OK để tiếp tục hoặc Hủy để thoát.

; *** Startup questions
PrivilegesRequiredOverrideTitle=Chọn chế độ cài đặt
PrivilegesRequiredOverrideInstruction=Chọn chế độ cài đặt
PrivilegesRequiredOverrideText1=%1 có thể được cài đặt cho tất cả người dùng (yêu cầu quyền quản trị viên), hoặc chỉ cho riêng bạn.
PrivilegesRequiredOverrideText2=%1 có thể được cài đặt chỉ cho riêng bạn, hoặc cho tất cả người dùng (yêu cầu quyền quản trị viên).
PrivilegesRequiredOverrideAllUsers=Cài đặt cho &tất cả người dùng
PrivilegesRequiredOverrideAllUsersRecommended=Cài đặt cho &tất cả người dùng (khuyên dùng)
PrivilegesRequiredOverrideCurrentUser=Chỉ cài đặt cho &tôi
PrivilegesRequiredOverrideCurrentUserRecommended=Chỉ cài đặt cho &tôi (khuyên dùng)

; *** Misc. errors
ErrorCreatingDir=Trình cài đặt không thể tạo thư mục "%1"
ErrorTooManyFilesInDir=Không thể tạo tệp trong thư mục "%1" vì thư mục chứa quá nhiều tệp

; *** Setup common messages
ExitSetupTitle=Thoát cài đặt
ExitSetupMessage=Quá trình cài đặt chưa hoàn tất. Nếu bạn thoát bây giờ, chương trình sẽ không được cài đặt.%n%nBạn có thể chạy lại trình cài đặt vào lúc khác để hoàn tất.%n%nBạn có muốn thoát cài đặt?
AboutSetupMenuItem=&Thông tin về trình cài đặt...
AboutSetupTitle=Thông tin về trình cài đặt
AboutSetupMessage=%1 phiên bản %2%n%3%n%nTrang chủ %1:%n%4
AboutSetupNote=
TranslatorNote=

; *** Buttons
ButtonBack=< &Quay lại
ButtonNext=&Tiếp theo >
ButtonInstall=&Cài đặt
ButtonOK=OK
ButtonCancel=Hủy
ButtonYes=&Có
ButtonYesToAll=Có cho &tất cả
ButtonNo=&Không
ButtonNoToAll=Khô&ng cho tất cả
ButtonFinish=&Hoàn tất
ButtonBrowse=&Duyệt...
ButtonWizardBrowse=D&uyệt...
ButtonNewFolder=&Tạo thư mục mới

; *** "Select Language" dialog messages
SelectLanguageTitle=Chọn ngôn ngữ cài đặt
SelectLanguageLabel=Chọn ngôn ngữ sẽ sử dụng trong quá trình cài đặt.

; *** Common wizard text
ClickNext=Bấm Tiếp theo để tiếp tục, hoặc Hủy để thoát trình cài đặt.
BeveledLabel=
BrowseDialogTitle=Duyệt tìm thư mục
BrowseDialogLabel=Chọn một thư mục trong danh sách bên dưới, sau đó bấm OK.
NewFolderName=Thư mục mới

; *** "Welcome" wizard page
WelcomeLabel1=Chào mừng đến với trình cài đặt [name]
WelcomeLabel2=Trình cài đặt sẽ cài đặt [name/ver] trên máy tính của bạn.%n%nBạn nên đóng tất cả các ứng dụng khác trước khi tiếp tục.

; *** "Password" wizard page
WizardPassword=Mật khẩu
PasswordLabel1=Quá trình cài đặt này được bảo vệ bằng mật khẩu.
PasswordLabel3=Vui lòng nhập mật khẩu, sau đó bấm Tiếp theo để tiếp tục. Mật khẩu có phân biệt chữ hoa chữ thường.
PasswordEditLabel=&Mật khẩu:
IncorrectPassword=Mật khẩu bạn đã nhập không chính xác. Vui lòng thử lại.

; *** "License Agreement" wizard page
WizardLicense=Thỏa thuận người dùng
LicenseLabel=Vui lòng đọc các thông tin quan trọng sau trước khi tiếp tục.
LicenseLabel3=Vui lòng đọc Thỏa thuận người dùng sau. Bạn phải chấp nhận các điều khoản của thỏa thuận này trước khi tiếp tục cài đặt.
LicenseAccepted=Tôi &chấp nhận thỏa thuận
LicenseNotAccepted=Tôi &không chấp nhận thỏa thuận

; *** "Information" wizard pages
WizardInfoBefore=Thông tin
InfoBeforeLabel=Vui lòng đọc thông tin quan trọng sau trước khi tiếp tục.
InfoBeforeClickLabel=Khi bạn đã sẵn sàng tiếp tục cài đặt, hãy bấm Tiếp theo.
WizardInfoAfter=Thông tin
InfoAfterLabel=Vui lòng đọc thông tin quan trọng sau trước khi tiếp tục.
InfoAfterClickLabel=Khi bạn đã sẵn sàng tiếp tục cài đặt, hãy bấm Tiếp theo.

; *** "User Information" wizard page
WizardUserInfo=Thông tin người dùng
UserInfoDesc=Vui lòng nhập thông tin của bạn.
UserInfoName=&Tên người dùng:
UserInfoOrg=&Tổ chức:
UserInfoSerial=&Số sê-ri:
UserInfoNameRequired=Bạn phải nhập tên.

; *** "Select Destination Location" wizard page
WizardSelectDir=Chọn thư mục cài đặt
SelectDirDesc=Bạn muốn cài đặt [name] vào đâu?
SelectDirLabel3=Trình cài đặt sẽ cài đặt [name] vào thư mục sau.
SelectDirBrowseLabel=Để tiếp tục, bấm Tiếp theo. Nếu bạn muốn chọn thư mục khác, bấm Duyệt.
DiskSpaceGBLabel=Cần ít nhất [gb] GB dung lượng trống trên đĩa.
DiskSpaceMBLabel=Cần ít nhất [mb] MB dung lượng trống trên đĩa.
CannotInstallToNetworkDrive=Trình cài đặt không thể cài đặt vào ổ đĩa mạng.
CannotInstallToUNCPath=Trình cài đặt không thể cài đặt vào đường dẫn UNC.
InvalidPath=Bạn phải nhập đường dẫn đầy đủ có ký tự ổ đĩa; ví dụ:%n%nC:\APP%n%nhoặc đường dẫn UNC có định dạng:%n%n\\server\share
InvalidDrive=Ổ đĩa hoặc vị trí chia sẻ UNC bạn đã chọn không tồn tại hoặc không thể truy cập. Vui lòng chọn vị trí khác.
DiskSpaceWarningTitle=Không đủ dung lượng đĩa
DiskSpaceWarning=Trình cài đặt yêu cầu ít nhất %1 KB dung lượng trống để cài đặt, nhưng ổ đĩa đã chọn chỉ còn %2 KB khả dụng.%n%nBạn có muốn tiếp tục không?
DirNameTooLong=Tên thư mục hoặc đường dẫn quá dài.
InvalidDirName=Tên thư mục không hợp lệ.
BadDirName32=Tên thư mục không được chứa bất kỳ ký tự nào sau đây:%n%n%1
DirExistsTitle=Thư mục đã tồn tại
DirExists=Thư mục:%n%n%1%n%nđã tồn tại. Bạn vẫn muốn cài đặt vào thư mục đó chứ?
DirDoesntExistTitle=Thư mục không tồn tại
DirDoesntExist=Thư mục:%n%n%1%n%nkhông tồn tại. Bạn có muốn tạo thư mục này không?

; *** "Select Components" wizard page
WizardSelectComponents=Chọn thành phần
SelectComponentsDesc=Những thành phần nào sẽ được cài đặt?
SelectComponentsLabel2=Chọn các thành phần bạn muốn cài đặt; bỏ chọn các thành phần bạn không muốn cài đặt. Bấm Tiếp theo khi sẵn sàng tiếp tục.
FullInstallation=Cài đặt đầy đủ
CompactInstallation=Cài đặt thu gọn
CustomInstallation=Cài đặt tùy chỉnh
NoUninstallWarningTitle=Thành phần đã tồn tại
NoUninstallWarning=Trình cài đặt phát hiện các thành phần sau đã được cài đặt trên máy tính của bạn:%n%n%1%n%nBỏ chọn các thành phần này sẽ không gỡ cài đặt chúng.%n%nBạn vẫn muốn tiếp tục chứ?
ComponentSize1=%1 KB
ComponentSize2=%1 MB
ComponentsDiskSpaceGBLabel=Các mục đã chọn cần ít nhất [gb] GB dung lượng đĩa.
ComponentsDiskSpaceMBLabel=Các mục đã chọn cần ít nhất [mb] MB dung lượng đĩa.

; *** "Select Additional Tasks" wizard page
WizardSelectTasks=Chọn tác vụ bổ sung
SelectTasksDesc=Bạn muốn thực hiện những tác vụ bổ sung nào?
SelectTasksLabel2=Chọn các tác vụ bổ sung mà bạn muốn trình cài đặt thực hiện khi cài đặt [name], sau đó bấm Tiếp theo.

; *** "Select Start Menu Folder" wizard page
WizardSelectProgramGroup=Chọn thư mục Start Menu
SelectStartMenuFolderDesc=Trình cài đặt nên đặt các phím tắt của chương trình ở đâu?
SelectStartMenuFolderLabel3=Trình cài đặt sẽ tạo các phím tắt của chương trình trong thư mục Start Menu sau.
SelectStartMenuFolderBrowseLabel=Để tiếp tục, bấm Tiếp theo. Nếu bạn muốn chọn thư mục khác, bấm Duyệt.
MustEnterGroupName=Bạn phải nhập tên thư mục.
GroupNameTooLong=Tên thư mục hoặc đường dẫn quá dài.
InvalidGroupName=Tên thư mục không hợp lệ.
BadGroupName=Tên thư mục không được chứa bất kỳ ký tự nào sau đây:%n%n%1
NoProgramGroupCheck2=&Không tạo thư mục Start Menu

; *** "Ready to Install" wizard page
WizardReady=Sẵn sàng cài đặt
ReadyLabel1=Trình cài đặt đã sẵn sàng bắt đầu cài đặt [name] trên máy tính của bạn.
ReadyLabel2a=Bấm Cài đặt để tiếp tục cài đặt, hoặc bấm Quay lại nếu bạn muốn xem lại hoặc thay đổi thiết lập.
ReadyLabel2b=Bấm Cài đặt để tiếp tục cài đặt.
ReadyMemoUserInfo=Thông tin người dùng:
ReadyMemoDir=Thư mục cài đặt:
ReadyMemoType=Loại cài đặt:
ReadyMemoComponents=Thành phần đã chọn:
ReadyMemoGroup=Thư mục Start Menu:
ReadyMemoTasks=Tác vụ bổ sung:

; *** TDownloadWizardPage wizard page and DownloadTemporaryFile
DownloadingLabel2=Đang tải xuống các tệp...
ButtonStopDownload=&Dừng tải xuống
StopDownload=Bạn có chắc chắn muốn dừng tải xuống không?
ErrorDownloadAborted=Quá trình tải xuống đã bị hủy
ErrorDownloadFailed=Tải xuống thất bại: %1 %2
ErrorDownloadSizeFailed=Lấy kích thước thất bại: %1 %2
ErrorProgress=Tiến trình không hợp lệ: %1 trên %2
ErrorFileSize=Kích thước tệp không hợp lệ: dự kiến %1, thực tế %2

; *** TExtractionWizardPage wizard page and ExtractArchive
ExtractingLabel=Đang giải nén các tệp...
ButtonStopExtraction=&Dừng giải nén
StopExtraction=Bạn có chắc chắn muốn dừng giải nén không?
ErrorExtractionAborted=Quá trình giải nén đã bị hủy
ErrorExtractionFailed=Giải nén thất bại: %1

; *** Archive extraction failure details
ArchiveIncorrectPassword=Mật khẩu không chính xác
ArchiveIsCorrupted=Tệp nén bị hỏng
ArchiveUnsupportedFormat=Định dạng tệp nén không được hỗ trợ

; *** "Preparing to Install" wizard page
WizardPreparing=Đang chuẩn bị cài đặt
PreparingDesc=Trình cài đặt đang chuẩn bị cài đặt [name] trên máy tính của bạn.
PreviousInstallNotCompleted=Quá trình cài đặt/gỡ bỏ của một chương trình trước đó chưa hoàn tất. Bạn cần khởi động lại máy tính để hoàn tất.%n%nSau khi khởi động lại máy tính, hãy chạy lại trình cài đặt để hoàn tất cài đặt [name].
CannotContinue=Trình cài đặt không thể tiếp tục. Vui lòng bấm Hủy để thoát.
ApplicationsFound=Các ứng dụng sau đang sử dụng các tệp cần được cập nhật bởi trình cài đặt. Khuyến nghị bạn cho phép trình cài đặt tự động đóng các ứng dụng này.
ApplicationsFound2=Các ứng dụng sau đang sử dụng các tệp cần được cập nhật bởi trình cài đặt. Khuyến nghị bạn cho phép trình cài đặt tự động đóng các ứng dụng này. Sau khi cài đặt hoàn tất, trình cài đặt sẽ cố gắng khởi động lại các ứng dụng.
CloseApplications=&Tự động đóng các ứng dụng
DontCloseApplications=&Không đóng các ứng dụng
ErrorCloseApplications=Trình cài đặt không thể tự động đóng tất cả các ứng dụng. Khuyến nghị bạn đóng tất cả các ứng dụng đang sử dụng các tệp cần cập nhật trước khi tiếp tục.
PrepareToInstallNeedsRestart=Trình cài đặt phải khởi động lại máy tính. Sau khi khởi động lại, hãy chạy lại trình cài đặt để hoàn tất cài đặt [name].%n%nBạn có muốn khởi động lại ngay bây giờ không?

; *** "Installing" wizard page
WizardInstalling=Đang cài đặt
InstallingLabel=Vui lòng đợi trong khi trình cài đặt cài đặt [name] trên máy tính của bạn.

; *** "Setup Completed" wizard page
FinishedHeadingLabel=Hoàn tất Cài đặt [name]
FinishedLabelNoIcons=Trình cài đặt đã hoàn tất cài đặt [name] trên máy tính của bạn.
FinishedLabel=Trình cài đặt đã hoàn tất cài đặt [name] trên máy tính của bạn. Ứng dụng có thể được khởi chạy bằng cách chọn các phím tắt đã tạo.
ClickFinish=Bấm Hoàn tất để thoát trình cài đặt.
FinishedRestartLabel=Để hoàn tất cài đặt [name], trình cài đặt cần khởi động lại máy tính của bạn. Bạn có muốn khởi động lại ngay bây giờ không?
FinishedRestartMessage=Để hoàn tất cài đặt [name], trình cài đặt cần khởi động lại máy tính của bạn.%n%nBạn có muốn khởi động lại ngay bây giờ không?
ShowReadmeCheck=Có, tôi muốn xem tệp README
YesRadio=&Có, khởi động lại máy tính ngay bây giờ
NoRadio=&Không, tôi sẽ tự khởi động lại máy tính sau
RunEntryExec=Chạy %1
RunEntryShellExec=Xem %1

; *** "Setup Needs the Next Disk" stuff
ChangeDiskTitle=Trình cài đặt cần đĩa tiếp theo
SelectDiskLabel2=Vui lòng đưa Đĩa %1 vào và bấm OK.%n%nNếu các tệp trên đĩa này có thể tìm thấy ở một thư mục khác, hãy nhập đường dẫn chính xác hoặc bấm Duyệt.
PathLabel=&Đường dẫn:
FileNotInDir2=Không thể tìm thấy tệp "%1" trong "%2". Vui lòng đưa đúng đĩa vào hoặc chọn thư mục khác.
SelectDirectoryLabel=Vui lòng chỉ định vị trí của đĩa tiếp theo.

; *** Installation phase messages
SetupAborted=Quá trình cài đặt chưa hoàn tất.%n%nVui lòng khắc phục sự cố và chạy lại trình cài đặt.
AbortRetryIgnoreSelectAction=Chọn thao tác
AbortRetryIgnoreRetry=&Thử lại
AbortRetryIgnoreIgnore=&Bỏ qua lỗi và tiếp tục
AbortRetryIgnoreCancel=Hủy cài đặt
RetryCancelSelectAction=Chọn thao tác
RetryCancelRetry=&Thử lại
RetryCancelCancel=Hủy

; *** Installation status messages
StatusClosingApplications=Đang đóng các ứng dụng...
StatusCreateDirs=Đang tạo các thư mục...
StatusExtractFiles=Đang giải nén các tệp...
StatusDownloadFiles=Đang tải xuống các tệp...
StatusCreateIcons=Đang tạo các phím tắt...
StatusCreateIniEntries=Đang tạo các mục INI...
StatusCreateRegistryEntries=Đang tạo các mục registry...
StatusRegisterFiles=Đang đăng ký các tệp...
StatusSavingUninstall=Đang lưu thông tin gỡ cài đặt...
StatusRunProgram=Đang hoàn tất cài đặt...
StatusRestartingApplications=Đang khởi động lại các ứng dụng...
StatusRollback=Đang hoàn tác các thay đổi...

; *** Misc. errors
ErrorInternal2=Lỗi nội bộ: %1
ErrorFunctionFailedNoCode=%1 thất bại
ErrorFunctionFailed=%1 thất bại; mã lỗi %2
ErrorFunctionFailedWithMessage=%1 thất bại; mã lỗi %2.%n%3
ErrorExecutingProgram=Không thể thực thi tệp:%n%1

; *** Registry errors
ErrorRegOpenKey=Lỗi khi mở khóa registry:%n%1\%2
ErrorRegCreateKey=Lỗi khi tạo khóa registry:%n%1\%2
ErrorRegWriteKey=Lỗi khi ghi vào khóa registry:%n%1\%2

; *** INI errors
ErrorIniEntry=Lỗi khi tạo mục INI trong tệp "%1".

; *** File copying errors
FileAbortRetryIgnoreSkipNotRecommended=&Bỏ qua tệp này (không khuyên dùng)
FileAbortRetryIgnoreIgnoreNotRecommended=&Bỏ qua lỗi và tiếp tục (không khuyên dùng)
SourceIsCorrupted=Tệp nguồn bị hỏng
SourceDoesntExist=Tệp nguồn "%1" không tồn tại
SourceVerificationFailed=Xác minh tệp nguồn thất bại: %1
VerificationSignatureDoesntExist=Tệp chữ ký "%1" không tồn tại
VerificationSignatureInvalid=Tệp chữ ký "%1" không hợp lệ
VerificationKeyNotFound=Tệp chữ ký "%1" sử dụng khóa không xác định
VerificationFileNameIncorrect=Tên tệp không chính xác
VerificationFileTagIncorrect=Thẻ định danh tệp không chính xác
VerificationFileSizeIncorrect=Kích thước tệp không chính xác
VerificationFileHashIncorrect=Mã băm của tệp không chính xác
ExistingFileReadOnly2=Không thể thay thế tệp hiện có vì tệp được đánh dấu chỉ đọc.
ExistingFileReadOnlyRetry=&Xóa thuộc tính chỉ đọc và thử lại
ExistingFileReadOnlyKeepExisting=&Giữ lại tệp hiện có
ErrorReadingExistingDest=Đã xảy ra lỗi khi cố gắng đọc tệp hiện có:
FileExistsSelectAction=Chọn thao tác
FileExists2=Tệp đã tồn tại.
FileExistsOverwriteExisting=&Ghi đè tệp hiện có
FileExistsKeepExisting=&Giữ lại tệp hiện có
FileExistsOverwriteOrKeepAll=&Áp dụng cho các xung đột tiếp theo
ExistingFileNewerSelectAction=Chọn thao tác
ExistingFileNewer2=Tệp hiện có mới hơn tệp mà trình cài đặt đang cố gắng cài đặt.
ExistingFileNewerOverwriteExisting=&Ghi đè tệp hiện có
ExistingFileNewerKeepExisting=&Giữ lại tệp hiện có (khuyên dùng)
ExistingFileNewerOverwriteOrKeepAll=&Áp dụng cho các xung đột tiếp theo
ErrorChangingAttr=Đã xảy ra lỗi khi cố gắng thay đổi thuộc tính của tệp hiện có:
ErrorCreatingTemp=Đã xảy ra lỗi khi cố gắng tạo tệp trong thư mục đích:
ErrorReadingSource=Đã xảy ra lỗi khi cố gắng đọc tệp nguồn:
ErrorCopying=Đã xảy ra lỗi khi cố gắng sao chép một tệp:
ErrorDownloading=Đã xảy ra lỗi khi cố gắng tải xuống một tệp:
ErrorExtracting=Đã xảy ra lỗi khi cố gắng giải nén một tệp nén:
ErrorReplacingExistingFile=Đã xảy ra lỗi khi cố gắng thay thế tệp hiện có:
ErrorRestartReplace=RestartReplace thất bại:
ErrorRenamingTemp=Đã xảy ra lỗi khi cố gắng đổi tên một tệp trong thư mục đích:
ErrorRegisterServer=Không thể đăng ký DLL/OCX: %1
ErrorRegSvr32Failed=RegSvr32 thất bại với mã thoát %1
ErrorRegisterTypeLib=Không thể đăng ký thư viện kiểu: %1

; *** Uninstall display name markings
UninstallDisplayNameMark=%1 (%2)
UninstallDisplayNameMarks=%1 (%2, %3)
UninstallDisplayNameMark32Bit=32-bit
UninstallDisplayNameMark64Bit=64-bit
UninstallDisplayNameMarkAllUsers=Tất cả người dùng
UninstallDisplayNameMarkCurrentUser=Người dùng hiện tại

; *** Post-installation errors
ErrorOpeningReadme=Đã xảy ra lỗi khi cố gắng mở tệp README.
ErrorRestartingComputer=Trình cài đặt không thể khởi động lại máy tính. Vui lòng thực hiện thao tác này thủ công.

; *** Uninstaller messages
UninstallNotFound=Tệp "%1" không tồn tại. Không thể gỡ cài đặt.
UninstallOpenError=Không thể mở tệp "%1". Không thể gỡ cài đặt
UninstallUnsupportedVer=Tệp nhật ký gỡ cài đặt "%1" có định dạng không được hỗ trợ bởi phiên bản này. Không thể gỡ cài đặt
UninstallUnknownEntry=Phát hiện mục không xác định (%1) trong nhật ký gỡ cài đặt
ConfirmUninstall=Bạn có chắc chắn muốn gỡ cài đặt hoàn toàn %1 và tất cả các thành phần của chương trình không?
UninstallOnlyOnWin64=Bản cài đặt này chỉ có thể gỡ trên Windows 64-bit.
OnlyAdminCanUninstall=Bản cài đặt này chỉ có thể gỡ bởi người dùng có quyền quản trị viên.
UninstallStatusLabel=Vui lòng đợi trong khi %1 được xóa khỏi máy tính của bạn.
UninstalledAll=%1 đã được xóa thành công khỏi máy tính của bạn.
UninstalledMost=Gỡ cài đặt %1 hoàn tất.%n%nMột số mục không thể xóa được. Bạn có thể xóa chúng thủ công.
UninstalledAndNeedsRestart=Để hoàn tất gỡ cài đặt %1, máy tính của bạn cần được khởi động lại.%n%nBạn có muốn khởi động lại ngay bây giờ không?
UninstallDataCorrupted=Tệp "%1" bị hỏng. Không thể gỡ cài đặt

; *** Uninstallation phase messages
ConfirmDeleteSharedFileTitle=Xóa tệp dùng chung?
ConfirmDeleteSharedFile2=Hệ thống cho biết tệp dùng chung sau không còn được sử dụng bởi bất kỳ chương trình nào. Bạn có muốn trình gỡ cài đặt xóa tệp dùng chung này không?%n%nNếu bất kỳ chương trình nào vẫn đang sử dụng tệp này và tệp bị xóa, các chương trình đó có thể không hoạt động bình thường. Nếu bạn không chắc chắn, hãy chọn Không. Để lại tệp trên hệ thống sẽ không gây hại gì.
SharedFileNameLabel=Tên tệp:
SharedFileLocationLabel=Vị trí:
WizardUninstalling=Trạng thái gỡ cài đặt
StatusUninstalling=Đang gỡ cài đặt %1...

; *** Shutdown block reasons
ShutdownBlockReasonInstallingApp=Đang cài đặt %1.
ShutdownBlockReasonUninstallingApp=Đang gỡ cài đặt %1.

; The custom messages below aren't used by Setup itself, but if you make
; use of them in your scripts, you'll want to translate them.

[CustomMessages]

NameAndVersion=%1 phiên bản %2
AdditionalIcons=Phím tắt bổ sung:
CreateDesktopIcon=Tạo biểu tượng ngoài màn hình &chính (Desktop)
CreateQuickLaunchIcon=Tạo biểu tượng &Quick Launch
ProgramOnTheWeb=%1 trên Web
UninstallProgram=Gỡ cài đặt %1
LaunchProgram=Khởi chạy %1
AssocFileExtension=&Liên kết %1 với phần mở rộng tệp %2
AssocingFileExtension=Đang liên kết %1 với phần mở rộng tệp %2...
AutoStartProgramGroupDescription=Khởi động cùng hệ thống:
AutoStartProgram=Tự động khởi động %1
AddonHostProgramNotFound=Không tìm thấy %1 trong thư mục bạn đã chọn.%n%nBạn vẫn muốn tiếp tục chứ?
