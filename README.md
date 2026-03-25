# ToDoDo

WPF 기반 데스크톱 ToDo 앱입니다.

## 포함된 핵심 변경점
- 반투명 글래스 카드 UI
- 헤더 드래그로 창 이동
- 그룹/할 일 이름 변경: 더블 클릭, F2, 우클릭
- 다중 선택 및 Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+A / Ctrl+N
- 그룹별 작업 관리
- 오늘 / 이번 주 / 나중에 필터
- 마감일 / 반복 / 우선순위 표시
- 완료 시 메타 정보 자동 접기
- 그룹 삭제 확인 오버레이
- JSON 자동 저장
- fonts 폴더 재귀 탐색으로 Pretendard 우선 적용

## 빌드
```powershell
dotnet build .\ToDoDo.csproj -c Release
```
