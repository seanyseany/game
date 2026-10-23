# 상점 · 포장 · 배송 설정

`Assets/Scenes/Village.unity`의 기존 **Store / Box 버튼은 연결 완료**입니다. 별도의 Button OnClick 등록은 필요 없습니다.

기본 프리팹은 `Assets/Prefabs/Tycoon/StoreIn.prefab`, `BoxIn.prefab`입니다. 프리팹 모드에서 아래 위치 자식들을 옮기면 됩니다. 위치에는 Project 창의 별도 프리팹 에셋 대신 **해당 창 안에 있는 자식 Transform**을 연결하세요.

## 위치 조정

| 프리팹 | 조정할 항목 |
| --- | --- |
| StoreIn | 각 `ResourcePosition`, `PricePosition`, `ReceiptPosition`, X 자식 |
| Receipt | `PhotoPosition`, `QuantityPosition`, `BoxPosition` |
| BoxIn | `BoxCompletePosition1` ~ `7`, X 자식 |
| boxComplete | `PhotoPosition` — 각 자원별로 서로 다른 위치를 지정해도 됩니다. |
| Village 씬의 Box 버튼 | `NumberPosition` — 보관 중인 박스 개수 표시 |

Store / Box 버튼의 **Panel Position**에는 패널이 나타날 위치를 등록합니다. Canvas 안의 `PanelPosition` 자식 RectTransform도 사용할 수 있습니다. 지정점을 움직이면 패널도 따라갑니다. 비워두면 화면 중앙에 표시합니다. 이전 `Panel Parent` 등록값은 자동으로 유지됩니다.

SpriteRenderer 패널은 Canvas의 작은 배율을 상속받지 않도록 월드에 생성하고, 지정점의 화면 좌표에 맞춥니다. 위치 오브젝트 아래에 패널이 자식으로 생성되지 않아도 정상입니다.

`BoxIn > Box Complete Positions`의 서로 다른 유효 위치 수가 보관함 용량입니다. 왼쪽 위치부터 넣으세요. 기본은 7칸이며, 숫자 프리팹 리스트는 **0번째 = 1, …, 6번째 = 7**입니다. 0개일 때 숫자는 숨깁니다. 숫자 프리팹이 없는 수량은 텍스트로 표시합니다.

BoxIn에는 **X Prefab / Box Complete Prefab / Box Complete Positions / Delivered Box Prefab**을 등록합니다. 드래그할 때는 boxComplete 자체를 움직이고, 건물에 놓은 뒤에는 별도로 등록한 Delivered Box Prefab을 표시합니다. 기본 연결은 기존 Box 프리팹이며 원하는 배달 전용 프리팹으로 바꿀 수 있습니다. 사진·건물·사진 위치는 **boxComplete의 Receipt Photos 리스트**에 등록합니다. `On Delivered` 목록이나 함수 연결은 필요 없습니다.

창의 카메라 위치와 표시 순서는 내부에서 자동 처리합니다. `DeliveryPanel`은 직접 추가하거나 설정할 필요 없습니다.

## StoreIn 등장 연출

StoreIn Inspector의 **등장 연출**에서 `Background Up`과 `Background Owner`를 등록합니다. 기존 자식 오브젝트와 프리팹 에셋을 모두 사용할 수 있으며, 기본 StoreIn 프리팹의 자식들은 연결되어 있습니다.

열 때마다 Receipt는 원래 위치의 Y +1에서, BackgroundUp은 Y -0.5에서 1초 동안 원래 위치로 이동합니다. Inspector에 배치한 위치를 기준으로 하므로 반복해서 열어도 위치가 누적되지 않습니다. BackgroundOwner는 매번 `1`(Standing), `2`(Napping), `3`(Sleeping) Trigger 중 하나를 무작위로 실행합니다. 같은 값이 연속으로 선택되어도 처음부터 재생합니다.

## 자원 설정

**StoreIn Inspector > 숫자 스프라이트**에서 직접 Sprite를 등록합니다. `Number Sprites`의 **0번째=숫자0, …, 9번째=숫자9**입니다. `Number Scale`로 가격과 영수증 수량의 크기를 조절합니다. 1은 기본 크기, 2는 2배입니다. 여러 자리 가격과 `1/12` 같은 수량도 등록한 숫자를 조합해 표시합니다.

숫자는 등록한 0~9 중 가장 넓은 숫자를 기준으로 같은 폭의 칸에 중앙 배치합니다. `111`과 `000`은 자리 간격과 전체 폭이 같으며, 각 스프라이트의 원래 가로세로 비율은 유지합니다.

`Slash Sprite`와 `Question Mark Sprite`는 선택 항목입니다. 비워도 `/`, `?`를 기본 스프라이트 도형으로 표시합니다. 미등록 숫자도 기본 숫자 도형으로 표시하므로 UI Text나 글꼴은 필요 없습니다. 월드 위치에는 SpriteRenderer, Canvas 안의 위치에는 UI Image로 표시합니다. Play 중 스프라이트나 크기를 바꿔도 담긴 수량은 유지합니다.

각 `*Resource.prefab`의 **Tycoon.Resources**에서 가격(`Energy Price`), 건물 프리팹 루트(`Building Prefab`), 수량(`Required Quantity`), 일반 영수증 사진(`Receipt Photo Prefab`)을 수정하세요. 기본 가격은 **에너지 1**, 수량은 **4**입니다.

- BreadResource → Bread 건물 → bread 사진 → breadBoxComplete 사진
- ChickenResource → ChickenTruck 건물
- CoffeeResource → Coffee 건물
- FruitResource → Fruit 건물
- IcecreamResource → Icecream 건물
- PharmacyResource → Pharmacy 건물 → medicine 사진
- SushiResource → 기존 Fish 건물 → sushi 사진

초밥에 다른 건물을 사용할 경우 **SushiResource의 Building Prefab**과 **boxComplete의 해당 Building Prefab**을 함께 바꾸세요. 일반 영수증 사진은 Resources에서, 포장 완료 사진은 boxComplete의 `Receipt Photos`에서 각각 등록합니다. 두 곳의 `Building Prefab`에는 Building 컴포넌트가 붙은 건물 프리팹의 루트 오브젝트를 등록합니다. 컴포넌트를 새로 불러와도 프리팹 연결이 유지되도록 루트에서 Building을 찾습니다.

`Resources`는 `Tycoon` 네임스페이스에 있어 기존 `UnityEngine.Resources`와 충돌하지 않습니다. Inspector의 Add Component에서 `Tycoon/Resources`로 추가할 수 있습니다.

## 동작

1. 실제 마을에 배치되고 건설이 끝난 건물의 자원만 열립니다. 구매 전·건설 중·업그레이드 중에는 자물쇠와 `?` 가격을 표시합니다. 건물 영업/급여 상태와는 별개입니다. 스크립트 재컴파일로 임시 참조가 사라진 경우에는 기존 자원·자물쇠·가격 표시를 다시 연결해 잠금 상태를 갱신합니다.
2. 드래그 가능한 자원을 누르면 보드의 자원이 0.3초 흔들립니다. 드래그 미리보기는 흔들리지 않습니다. 자원을 영수증에 놓았을 때만 **에너지**를 차감합니다. 에너지가 가격보다 부족하면 드래그 대신 누른 자원이 0.3초 좌우로 떨려 구매 불가를 알리고 제자리로 돌아옵니다. 이때 에너지나 산소를 차감하지 않습니다. 드래그 중 에너지/건물 상태가 바뀌면 놓는 시점에 다시 확인합니다. 기존에 등록한 가격은 그대로 유지됩니다.
3. 영수증은 **처음 넣은 자원 한 종류**로 고정합니다. 수량이 4면 `1/4`부터 `4/4`까지 네 번 담습니다. 다른 자원이나 초과 수량은 결제하지 않습니다. 정상적으로 물건을 담을 때 박스가 시각적 중심을 고정한 채 양쪽 대각선으로 기울어졌다가 돌아옵니다.
4. Full 상태에서 영수증의 박스를 누르면 보관함 자리를 먼저 예약하고 `pack` → 박스/사진 축소 → 보관함 등록 → 영수증 초기화 → 새 박스의 `boxopen` 순서로 처리합니다. 보관함이 가득 차면 수량과 결제를 유지합니다. 포장 중 창을 닫아도 처리는 이어집니다.
5. boxComplete를 누르고 드래그하면 **선택한 boxComplete 자체와 그 안의 영수증 사진을 함께 1.1배 확대**해 포인터를 따라 움직입니다. BoxIn은 잠시 숨지만 드래그 중인 항목과 사진은 계속 보입니다. 해당 건물 위에서 놓으면 보관함 항목을 제거하고 뒤의 항목을 왼쪽으로 옮기며 BoxIn을 다시 표시합니다. 드래그한 boxComplete와 사진은 건물 쪽에서 0.25초 동안 작아져 사라집니다. 이어서 `Delivered Box Prefab`이 그 건물의 기존 `Line2Point`에 생성되어 3초 동안 유지되고 1초 동안 투명해져 사라집니다. 이 연출은 BoxIn을 닫아도 이어지며, Delivered Box Prefab이 비어 있으면 상자를 원래 보관함 위치로 되돌립니다. 다른 곳에 놓거나 취소하면 같은 항목을 원래 위치·크기·표시 순서로 복구합니다.
6. 배달 박스가 완전히 사라진 다음 건물 게이지를 **1레벨: 전체의 1/3, 2레벨: 전체의 1/6**만큼 채웁니다. 최대치를 넘기지 않으며 빈 게이지도 다시 영업을 시작합니다. 시간 경과에 따른 자동 충전은 없습니다. 기존 시간 경과/고객 응대에 따른 소모는 유지됩니다. 게이지의 소수점 값도 저장됩니다. 연출 도중 건물 이동·업그레이드가 시작되면 충전량을 잃지 않고 해당 건물이 다시 준비될 때 적용합니다. 배달 연출 시간은 게임 시간 기준입니다.
7. 건물과 길을 클릭해도 기존 급여 관리 UI를 열지 않습니다. 건물을 마우스 또는 터치로 1.4초 길게 누르면 이동할 수 있습니다. 터치는 마우스 에뮬레이션 설정에 의존하지 않으며, 누른 손가락을 계속 추적합니다. 건물 자체와 게이지는 이동 입력을 막지 않고 외부 메뉴 UI만 입력을 막습니다. 고객 거래 완료 보상은 `Building`의 **Oxygen Value**만큼 산소를 지급합니다. 기존 Energy Value에 등록한 수치가 자동으로 이전됩니다.

Receipt의 자원별 `Photo Positions`가 한 개면 대표 사진 하나를 표시합니다. 드래그 횟수마다 사진을 추가하려면 위치를 여러 개 넣으세요. 개수는 항상 `Quantity Position`에 표시됩니다.

`PackingBox.controller`, `BoxPack.anim`, `BoxOpen.anim`은 제공된 스프라이트로 만든 기본 애니메이션입니다. 교체 시 Trigger 이름을 `pack`, `boxopen`으로 맞추거나 StoreIn의 Trigger 필드를 수정하세요. `Pack Animation Seconds`는 교체한 포장 애니메이션 길이에 맞추세요.

Store를 열 때마다 영수증은 등록된 위치보다 로컬 Y +1에서 시작해 1초 동안 원래 위치로 내려옵니다. 박스도 처음뿐 아니라 StoreIn을 열 때마다 `boxopen` 트리거로 열기 애니메이션을 다시 재생합니다. 기존 박스와 영수증 수량은 유지합니다. 제공된 Animator의 Open 전환은 같은 상태에서도 다시 시작할 수 있습니다.

Shop·Play·Store·Box는 하나만 열립니다. 네 버튼 중 다른 버튼을 누르면 기존 창이 닫히고 선택한 창만 열립니다. 설정·은행·터렛 등 다른 UI를 누르거나 열어도 기존 창이 닫힙니다. 각 창 안의 버튼·자원·영수증·포장 박스·보관함 항목은 해당 창을 유지합니다. Store·Box는 같은 버튼을 다시 누르면 해당 창이 닫힙니다.

이 창들이 열려 있는 동안에는 패널 밖을 눌러도 뒤의 건물·은행·터렛·산소 아이콘과 배경 드래그가 입력을 받지 않습니다. 창 안의 자원·박스와 다른 UI 버튼은 계속 사용할 수 있습니다. 창을 닫은 터치/클릭은 손을 뗄 때까지 UI가 처리해 뒤의 오브젝트에 전달하지 않습니다. boxComplete 배달 드래그와 건물에 놓기 판정은 유지됩니다. 별도 Inspector 등록은 필요하지 않습니다.

자원·포장 박스·완성 상자 위에 장식용 UI 이미지나 글자가 겹쳐도 조작할 수 있습니다. 실제 버튼과 스크롤 등 입력을 처리하는 UI는 우선하며, 다른 팝업의 배경은 그 아래 패널로 입력이 넘어가는 것을 막습니다. 입력 대상 판정과 창 전환에는 같은 결과를 사용합니다.

BoxIn의 포장된 상자는 자원 종류·수량·배달할 건물·보관 순서를 기존 마을 저장 파일에 기록합니다. 씬 이동과 게임 재실행 후 같은 마을의 BoxIn을 열면 사진과 내용물을 복원합니다. 배달 연출이 끝나 건물 게이지가 충전된 뒤 저장 목록에서도 제거하며, 연출 도중 씬을 떠나거나 게임을 종료하면 미완료 상자는 보관함으로 복구합니다. 마을 진행 초기화 시 상자 저장도 초기화합니다. 별도 Inspector 등록은 필요 없습니다.

StoreIn의 미포장 영수증은 같은 실행 중에 창을 닫거나 씬을 이동해도 유지됩니다. 복원할 때 에너지를 다시 차감하지 않습니다. 게임을 종료하면 이 미포장 내용만 초기화되며, BoxIn에 포장 완료된 상자는 남습니다. 에너지·산소·건물 게이지는 기존 VillageManagement 저장 방식을 따릅니다.

## 실행 비용

입력이 없을 때는 마을 입력 차단 검사에서 자원 렌더러와 UI를 다시 검색하지 않습니다. UI 레이캐스트의 목록과 이벤트 데이터를 재사용하고, 창 전환은 등록된 네 창만 확인합니다. 실행 중 건물 목록은 활성화/비활성화 때 관리하며 Store의 잠금 갱신에서는 한 번 모아 재사용합니다. 건설 완료·이동·구매 판정은 현재 상태를 계속 확인합니다.

패널의 텍스트 Canvas 목록은 구조가 바뀔 때 갱신하고, 위치·크기가 같으면 Transform을 다시 쓰지 않습니다. 숫자 스타일이 그대로이면 기존 숫자 오브젝트를 재사용하며, 스프라이트 배열 내부 변경과 크기 변경은 감지합니다. 닫힌 Play는 저장 이벤트마다 버튼 목록을 재생성하지 않고, 열린 Play도 목록 내용이 같으면 버튼을 재사용합니다.

VSync가 꺼져 있고 FPS도 지정하지 않은 PC 실행은 60 FPS를 목표로 합니다. 이미 지정된 FPS·VSync와 모바일 기본값은 유지하며, 상호작용 런타임이 파괴될 때 자신이 설정한 제한만 복구합니다.

Unity 6000.2.14f1의 임시 테스트 프로젝트에서 같은 조건으로 입력 차단 확인을 1,000회 반복한 측정은 약 15.54 ms / 335,070 B에서 0.18 ms / 42 B로 줄었습니다. 할당량은 Unity Profiler의 `GC.Alloc` 기록이며 측정 자체의 오버헤드를 포함합니다. 개별 반복 검사의 비교 수치로, 게임 전체 프레임 시간이나 컴퓨터 온도 측정은 아닙니다.

## 설정 도구와 검증

다른 씬이나 프리팹에 다시 연결할 때:

1. `Tools > Tycoon > 1. Prepare prefabs`
2. 해당 씬을 열고 `Tools > Tycoon > 2. Connect Store and Box buttons in open scene`

기존에 등록한 값과 위치는 유지하고 비어 있는 연결을 채웁니다. 씬 연결 후에는 씬을 저장하세요.

`Assets/Tests/Editor/TycoonWorkflowTests.cs`에 결제, 수량, 주문 사본, 보관함 예약, 포장, 애니메이션 연결, 배송 성공/실패/취소와 정렬, boxComplete·사진의 1.1배 드래그 및 복구, Canvas/월드의 패널 위치, 숫자 스프라이트 조합·크기 조절·고정 폭 중앙 배치를 검사하는 테스트가 있습니다. `TycoonPointerPlayTests.cs`에서는 실제 프리팹의 입력, 영수증의 씬 간 유지, 상자의 파일 저장·재실행 복원, 레벨별 배송 충전과 자동 충전 제거도 검증합니다. 실제 화면 배치와 모바일 터치는 위치 조정 후 Play Mode/기기에서 확인하세요.
