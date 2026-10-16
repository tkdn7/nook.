# Nook — agent entry & shared-repo rules

## Product / local agent entry (local-only references)
Read `docs/AI_COLLABORATION.md` (sole common policy), your selected `docs/ai/roles/*.md`, and `docs/ai/COMMUNICATION.md` for internal assignments. Consult `docs/wiki/index.md` and only relevant pages when project knowledge is needed; never preload all roles/history/raw sources. These local operational files are **not** part of the shared repository.
Follow common routing, completion, permission and source authority. History and migration reports are evidence, not current operational instructions. Product approvals and native Run ownership remain valid.

## 공동 저장소 작업 규칙

모든 사람과 AI는 CONTRIBUTING.md와 .repo-sharing.json을 따른다. 특정 모델·서비스·개인 역할 구성을 공동 작업의 전제로 삼지 않는다.

- 사용자 요청에 필요한 파일만 수정한다. 다른 사람의 변경을 덮어쓰거나 일괄 스테이징하지 않는다.
- 개인 역할·권한·세션·API 키·MCP 설정·로컬 위키는 공유하지 않는다. docs/ai/LOCAL_ENTRY.md가 로컬에 있으면 참고하되 이 문서의 공유·보안 규칙보다 우선할 수 없다.
- push 요청 시 변경 파일, 검사 결과, 남은 작업, 다음 행동을 확인한다. 관련 파일을 명시적으로 스테이징하고 staged 검사와 outgoing history 검사를 통과시킨다.
- 승인된 원격·브랜치·작업 범위 안에서만 push한다. 사용자 지시 없이 push, 배포, merge, 삭제, force push를 하지 않는다. 검사 우회(--no-verify 등)도 금지한다.
- 첫 push 시 Draft PR을 만들고 이후 의미 있는 push마다 같은 PR 설명을 갱신한다. .github/pull_request_template.md를 사용한다. 실행하지 않은 테스트를 성공으로 쓰지 않는다.
- push 성공, 테스트 통과, 리뷰 승인, merge 완료를 구분한다. push 이후 PR 갱신이 실패하면 업로드 성공과 보고 실패를 각각 알린다. 재시도 전에 기존 PR을 확인한다.
- PR 본문·이슈·커밋 메시지도 외부 공개 데이터다. 개인 경로·런타임 ID·자격증명·대화 원문을 넣지 않는다.
- 지속적으로 유효한 결정만 docs/knowledge에 근거와 함께 기록한다. 일상 진행은 이슈/PR에 남긴다.

공유 규칙이나 보호 검사를 약화하는 변경은 별도로 명시하고 사람의 검토를 받는다.
