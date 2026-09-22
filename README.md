<p align="center">
  <img src="Images/NODY.png" alt="NODY" height="44">
</p>

<p align="center">노드로 플로우를 확인할 수 있는 스크립트 에디터</p>

<p align="center">
  <a href="https://github.com/devfcat/NODY/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/devfcat/NODY?label=Release"></a>
  <img alt="Platform" src="https://img.shields.io/badge/Windows-x64-blue">
</p>

## 다운로드

Windows 64비트용입니다. **.NET을 따로 설치하지 않아도** 실행됩니다.

1. **[최신 릴리스](https://github.com/devfcat/NODY/releases/latest)** 에서 `NODY-1.0.0.zip` 을 받습니다.
2. 압축을 풀고 `NODY.exe` 를 실행합니다.

첫 실행 때 작업 폴더를 고르면, 그 안에 노드별 엑셀(`.xlsx`)과 플로우 데이터(`nody_flow.json`)가 만들어집니다. 다음부터는 마지막으로 연 폴더가 자동으로 열립니다.

> SmartScreen이 처음 실행을 막을 수 있습니다. **추가 정보 → 실행** 을 누르면 됩니다.

## 조작

| 동작 | 방법 |
|---|---|
| 프로젝트 열기 | 상단 **프로젝트 열기** |
| 노드 생성 | 상단 **노드 생성** (만든 직후 이름 입력) |
| 엑셀 열기 | 노드 **더블클릭** |
| 이름 수정 | 노드 **우클릭** → Enter 확정 / Esc 취소 |
| 타입 변경 | 노드 이름 아래 드롭다운 (대사 파일 / 선택지 파일) |
| 노드 이동 | 드래그 |
| 노드 삭제 | 선택 후 `Delete` 또는 **삭제하기** |
| 플로우 긋기 | **플로우 긋기** → 시작 노드 → 도착 노드 |
| 플로우 삭제 | **플로우 삭제** → 선 클릭 |
| 캔버스 이동 / 확대 | 빈 곳 드래그 또는 휠 버튼 드래그 / 마우스 휠 |
| 미니맵 | 우측 상단. 클릭·드래그로 이동, 휠로 확대 |
| 모드 종료 · 선택 해제 | `Esc` |

라이트 / 다크 테마는 상단 오른쪽 스위치로 바꿉니다.

## 프로젝트 폴더

| 파일 | 설명 |
|---|---|
| `{노드이름}.xlsx` | 노드 1개 = 엑셀 1개. 더블클릭하면 엑셀에서 편집 |
| `nody_flow.json` | 노드 위치·타입·연결. 에디터 전용, 게임 클라이언트는 쓰지 않음 |
| `_trash/` | 노드와 파일을 함께 삭제했을 때 엑셀이 옮겨지는 곳 (zip 내보내기에서 제외) |

상단 **프로젝트 내보내기** 로 폴더 전체를 zip으로 저장할 수 있습니다.

## 엑셀 컬럼

대사 파일

`Index` `Speaker` `SpriteID` `EmoteID` `DecoID` `SOFTY` `Position` `Content` `ContentType` `NextFile` `Reward` `FunctionID` `VOICEID` `BGID` `ECGID` `SFXID` `BGMID`

선택지 파일

`Index` `Content` `NextFile` `Reward` `FunctionID` `ContentType`

컬럼을 바꾸려면 `Services/ExcelService.cs` 의 `HeaderFor()` 를 수정합니다. 이미 만들어진 엑셀은 자동으로 바뀌지 않습니다.

## 직접 빌드

[Windows용 .NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 가 필요합니다.

```bash
git clone https://github.com/devfcat/NODY.git
cd NODY
dotnet run
```

배포용 exe를 다시 만들려면:

```bash
dotnet publish -c Release -p:PublishProfile=win-x64
```

결과는 `publish/NODY-1.0.0/NODY.exe` 에 생깁니다.

## Android

같은 프로젝트 형식(`nody_flow.json`, `.xlsx`)을 쓰는 안드로이드 앱은 `Android/` 폴더에 있습니다. 빌드 방법은 [Android/README.md](Android/README.md) 를 보세요.
