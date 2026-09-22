# NODY Android

Windows 앱과 같은 `nody_flow.json` / `.xlsx` 형식을 쓰는 안드로이드 에디터입니다.

## 할 수 있는 것

- 노드 생성·이동·삭제·이름/타입 변경
- 플로우 긋기 / 삭제
- 더블탭 또는 **스크립트**로 엑셀 내용 편집
- 미니맵, 핀치 줌, 드래그 패닝
- 프로젝트를 zip으로 공유 (PC NODY에서 이어서 열 수 있음)
- 라이트 / 다크

앱 전용 저장소 `projects/default` 에 프로젝트가 생깁니다.

## 빌드

[.NET 10 SDK](https://dotnet.microsoft.com/download)와 Android 워크로드가 필요합니다.

```bash
dotnet workload install maui-android
cd Android
dotnet build -t:InstallAndroidDependencies -p:AcceptAndroidSdkLicenses=True
dotnet build -f net10.0-android
```

에뮬레이터 또는 기기에 설치:

```bash
dotnet build -f net10.0-android -t:Install
```

Android SDK와 JDK 17이 필요합니다. 없으면 위 `InstallAndroidDependencies` 가 받아 줍니다.
