# Media Controller 네이티브 출력 장치

연결된 렌더 엔드포인트를 `IMMDeviceEnumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)`로 열거하고 `PKEY_Device_FriendlyName`·엔드포인트 ID·Multimedia 기본 장치를 읽습니다. 각 조회는 별도 COM 객체를 열고 모두 해제합니다.

기본 출력 변경은 Windows의 비공개 `IPolicyConfig` COM ABI를 격리해 사용합니다. Console/Multimedia/Communications 세 역할을 변경하고 중간 실패 시 변경된 역할을 원래 장치로 되돌리려고 시도합니다. 전체 목록과 실제 Multimedia 기본값을 다시 읽어 성공 여부를 확인합니다. 장치 ID는 전환 직전에 다시 확인합니다. Windows 업데이트로 ABI가 지원되지 않으면 오류 상태를 표시합니다.

공식 열거·속성 자료: [Microsoft Core Audio device properties](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-properties), [Enumerating audio devices](https://learn.microsoft.com/en-us/windows/win32/coreaudio/enumerating-audio-devices).

설정 COM 인터페이스의 원 조사: [EreTIk / DefSound](https://kitere.github.io/tools/DefSound.html). 이 자료는 문서화된 기본 출력 설정 API가 없음을 설명하며 CLSID와 IID를 제공합니다. 런타임 바인딩을 코드의 작은 별도 경계에 유지합니다.

검증은 실제 장치 열거·속성·기본 장치 읽기, 모의 장치 전환·연결 해제·실패·중복 클릭을 포함합니다. QA는 사용자의 실제 기본 출력 장치를 변경하지 않습니다. 실제 스피커/헤드폰 간 전환과 해당 앱의 반영 여부는 하드웨어 최종 확인 항목입니다.
