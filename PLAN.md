# UnitySceneGraph 개발 계획

유니티 하이어라키에 있는 3D/2D 렌더러 오브젝트를 자연어로 찾는 에디터 도구.
예: "책상 위에 있는 빨간 사과", "소파 밑에 숨어있는 검정 고양이"

환경: Unity 6000.3 / URP 17 / 에디터 전용

---

## 1. 목표와 비목표

**목표**
- Claude Code 같은 MCP 클라이언트에서 자연어로 씬 오브젝트를 찾아 선택한다 (Selection + ping).
- 이름이 쓸모없는 오브젝트("Cube (3)", "SM_Prop_0042")도 외형으로 찾는다.
- 오브젝트를 옮기거나 머티리얼/메시를 바꿔도 틀린 결과를 내지 않는다.
- LLM 없이 구조화 쿼리 JSON만 넣어도 똑같이 돌아가고, 그 상태로 테스트한다.

**비목표 (1차 범위 밖)**
- 빌드된 게임 안에서 검색하는 것. 코드는 전부 Editor asmdef에 있고 빌드에 안 들어간다. 에디터 Play Mode에서는 돌아가지만 보장하지 않는다.
- 씬 편집(이동, 생성). 검색과 선택만 한다.

---

## 2. 구조

```
 Claude Code ──stdio──▶ MCP Server (Node) ──HTTP──▶ Unity Editor
   (자연어 →                                        │
    구조화 쿼리 번역)                                 ▼
                                             QueryEngine
                                           ┌────┴─────┐
                                   SceneIndexer   SemanticDatabase
                                   (라이브 스캔:    (미리 만든 외형 정보:
                                    bounds, 이름,    명사, 색, 임베딩.
                                    계층, 참조)      입력 해시가 키)
                                                          ▲
                                                   LabelJobQueue
                                                  (썸네일 렌더 → CLIP)
                                                          ▲
                                                   DirtyTracker
                                            (ObjectChangeEvents,
                                             AssetPostprocessor, 씬 열기)
```

**원칙**
- LLM은 자연어를 구조화 쿼리로 바꾸는 일만 한다. 기하 판정은 안 한다.
- Unity는 쿼리를 한 번에 평가한다. MCP 왕복은 1회.
- 위치, bounds, 계층은 저장하지 않는다. 검색할 때 바로 읽는다. 그래서 옮겨도 깨질 게 없다.
- 외형 정보(명사, 색, 임베딩)만 미리 만든다. 입력 해시를 같이 저장해서 입력이 바뀌면 스스로 무효가 된다.

---

## 3. 데이터 모델

### 3.1 인덱스: 파츠와 그룹

인덱스는 검색 대상 목록이다. 씬을 한 번 훑어서 렌더러 오브젝트를 모아 놓은 것이고, 검색은 씬이 아니라 이 목록을 뒤진다. 저장하지 않고 검색할 때마다 다시 만든다. 수천 개라도 ms 단위다.

목록은 평면 리스트가 아니라 트리다. 노드는 두 종류다.

- **파츠**: 렌더러가 달린 오브젝트 하나. 전부 파츠다. 예외 없다.
- **그룹**: 파츠 여러 개를 "한 물체"로 묶은 것. 사과 = 몸체 + 꼭지, 책상 = 상판 + 다리 4개.

파츠는 그룹에 들어가도 사라지지 않는다. 둘 다 검색 대상이다.

#### 그룹이 하는 일
- 결과 정리. "책상"을 찾았을 때 다리 4개와 상판이 따로 나오지 않고 책상 하나로 나온다. 부모와 자식이 같이 걸리면 부모만 남긴다.
- 라벨링 단위. 다리 하나만 썸네일로 찍으면 모델이 뭔지 모른다. 그룹 단위로 찍어야 "책상"이 나온다. 그룹 썸네일은 서브트리 렌더러 전체(자식 그룹 포함)로 그린다. 직접 파츠가 없는 그룹도 빈 그림이 되지 않는다.

#### 후보 렌더러
대상: MeshRenderer, SkinnedMeshRenderer, SpriteRenderer.
제외:
- ParticleSystemRenderer, TrailRenderer, LineRenderer, VFX. 물체가 아니라 효과다.
- HideFlags가 걸린 오브젝트, Tag가 EditorOnly인 오브젝트.
- 비활성 오브젝트는 설정으로 정한다. 기본은 포함하고 결과에 비활성 표시를 붙인다.
- Terrain은 따로 노드 타입을 둔다. "바닥 위에"의 바닥 역할.

#### 그룹 만드는 규칙
파츠마다 씬 루트까지 조상을 훑는다. 처음 걸리는 데서 멈추지 않는다. 신호가 있는 조상은 전부 그룹이 되고, 가까운 그룹이 먼 그룹의 자식이 된다.

1. **SemanticTag 수동 지정. 절대 우선.** 조상 중 `isGroupRoot`가 켜진 게 있으면 가장 가까운 그것이 그룹이고, 그 아래 신호는 전부 무시한다. 그 위로는 규칙 2가 계속된다. isGroupRoot 책상이 Room 프리팹 안에 있으면 Room 그룹은 생긴다. `splitChildren`이 켜진 오브젝트는 규칙 2의 신호로 치지 않고, 규칙 3 휴리스틱이 올라올 때 스톱리스트처럼 그 직전에서 멈춘다. 아래 규칙이 틀릴 때 쓰는 탈출구.
2. **신호 조상 전부.** 올라가면서 아래 네 가지 중 하나라도 있는 조상을 모두 그룹 노드로 만든다. 같은 오브젝트에 신호가 여럿이면(프리팹 루트 + Animator) 그룹 하나다.
   - LODGroup. LOD0 렌더러만 파츠로 남기고 LOD1 이상은 버린다. bounds도 LOD0 기준.
   - Regular 또는 Variant 프리팹 인스턴스 루트. `PrefabUtility.GetPrefabAssetType`이 Model이면 신호가 아니다. FBX는 "한 파일에서 왔다"는 뜻일 뿐 "한 물체"라는 뜻이 아니다. 부품 FBX를 빈 오브젝트 밑에 모아 만든 책상이 5개로 쪼개지는 걸 막는다.
   - Rigidbody. 물리적으로 한 덩어리로 움직이는 단위.
   - Animator. 한 캐릭터로 움직이는 단위.
3. **휴리스틱 올라가기.** 파츠에서 부모로 한 단계씩 올라간다. 가장 가까운 신호 그룹 바로 아래까지만 간다. 신호 그룹이 없으면 씬 루트까지. 아래 세 조건을 다 만족하면 계속, 하나라도 깨지면 직전에서 멈춘다. 그룹은 하나만 만든다.
   - 부모 서브트리의 렌더러 수 ≤ maxParts (기본 24)
   - 부모 서브트리 bounds 대각선 ≤ maxDiagonal (기본 4m, 2D는 뷰포트 비율)
   - 부모 이름이 스톱리스트에 없고 splitChildren도 아님 (기본 스톱리스트: Environment, Props, Geometry, Furniture, Group, `---`, `[`로 시작)

   멈춘 자리가 다음 둘 중 하나면 중복이라 그룹을 만들지 않는다.
   - 파츠가 하나뿐이다. 본 뼈대 밑에 혼자 달린 무기처럼.
   - 멈춘 노드의 서브트리 렌더러 집합이 바로 위 신호 그룹의 서브트리 렌더러 집합(자식 신호 그룹 서브트리 제외)과 같다. 프리팹 안의 "Mesh" 같은 중간 홀더처럼. 소속 파츠가 아니라 서브트리로 비교해야 순환이 없다.
4. **렌더러 자신.** 그것도 없으면 파츠 하나가 곧 그룹.

휴리스틱은 신호 그룹을 넘어 올라가지 않는다. 넘어가면 Pets 빈 오브젝트 밑의 고양이 두 마리가 Pets로 또 묶이는 식의 쓸모없는 상위 그룹이 생긴다. 반대로 신호 그룹 안에서는 돈다. Shelf 프리팹 안에 플레인 메시로 넣은 Book_01/{Body, Cover}가 Book_01 그룹으로 묶여야 "선반 위의 빨간 책"이 된다.

**과대 그룹 분할.** 2에서 만든 프리팹 그룹의 서브트리 렌더러 수가 maxParts를 넘으면(방 전체 프리팹) 그 그룹은 지운다. 지워진 그룹은 신호가 아닌 것으로 친다. 그 밑 파츠들은 다른 신호가 없으면 규칙 3을 돌고, 300개 방이면 maxParts에서 알아서 멈춘다.

#### 소속 결정
파츠는 가장 가까운 그룹 소속이다. 바깥 그룹은 자식 그룹에 속한 파츠를 갖지 않는다. 책상 프리팹 안에 사과 프리팹이 있으면 책상 그룹은 상판과 다리만 갖고, 사과 그룹은 몸체와 꼭지를 갖는다. 선반 프리팹 안에 Rigidbody 책이 있으면 책마다 그룹이 생기고 선반 그룹의 자식이 된다. 어느 신호를 우선할지 고를 필요가 없다. 둘 다 그룹이다.

#### bounds
- 그룹 bounds = 서브트리 렌더러 전체의 `Renderer.bounds`(월드 AABB) 합집합. 소속 파츠와 자식 그룹을 다 넣는다. 직접 파츠가 없는 그룹도 bounds가 비지 않는다.
- **관계 판정 때 조상 제외.** `on(사과, 책상)`을 검사할 때 사과가 책상 서브트리 안에 있으면(상판에 자식으로 붙여 두는 일이 흔하다) 책상 bounds를 사과 서브트리만 뺀 채로 다시 계산한다. 다른 자식 그룹은 그대로 둔다. 이게 없으면 사과는 항상 책상 "inside"가 된다.
- 스킨드 메시는 `SkinnedMeshRenderer.bounds` 그대로. 포즈마다 바뀌지만 라이브 값이라 상관없다.

#### 예시
| 씬 구조 | 결과 |
|---|---|
| Desk 프리팹(Regular) 안에 Apple 프리팹(Regular) 중첩 | 둘 다 신호. Desk 그룹 {Top, Leg×4}, 그 자식으로 Apple 그룹 {Body, Stem}. Desk bounds에서 Apple 제외. on 성립 |
| Desk.prefab = 빈 루트 + DeskTop.prefab + Leg.prefab×4 (전부 Regular) | 경로의 신호 조상 전부가 그룹. DeskTop, Leg 그룹 5개가 Desk 그룹의 자식. Desk는 직접 파츠가 없어도 그룹이 된다 |
| 빈 오브젝트 Desk 밑에 SM_DeskTop.fbx, SM_DeskLeg.fbx×4 (Model) | Model은 신호 아님. 신호 없으니 휴리스틱. Desk까지 올라가고 Furniture(스톱리스트)에서 멈춤. Desk 그룹 하나. 썸네일도 책상 전체 |
| Room/Furniture/Desk/{Top, Leg×4, Apple} 전부 플레인 메시 | 휴리스틱. Desk 서브트리 6개, 작음 → 계속. Furniture 스톱리스트 → Desk 그룹. Apple은 Desk 그룹 안의 파츠. "apple"은 파츠에, "desk"는 그룹에 걸리고 조상 제외 bounds로 on 성립 |
| Cat.prefab(Regular, 루트에 Animator) 안에 CatBody.fbx, Collar.fbx(Model) | Model은 무시. Cat 루트가 프리팹이자 Animator라 그룹 하나. 몸과 목걸이가 한 고양이 |
| Shelf.prefab(Regular) 안에 Rigidbody 달린 플레인 메시 책들 | Book 그룹(Rigidbody)들이 Shelf 그룹(프리팹)의 자식. "선반 위의 빨간 책" 성립 |
| Shelf.prefab(Regular) 안에 Rigidbody 없는 플레인 메시 Book_01/{Body, Cover} | 휴리스틱이 Shelf 바로 아래까지 올라가 Book_01 그룹을 만든다. Shelf 그룹의 자식. 성립 |
| Cat.prefab 안 Armature/…/Hand/Sword 메시 하나 | 휴리스틱이 Armature까지 올라가지만 파츠가 하나라 그룹 안 만듦. Sword는 Cat 그룹의 파츠 |
| Desk.prefab/Mesh/{Top, Leg×4}, Mesh는 빈 홀더 | 휴리스틱이 Mesh에서 멈추지만 서브트리 렌더러 집합이 Desk 그룹과 같아 그룹 안 만듦 |
| 방 전체 프리팹, 렌더러 300개 | maxParts 초과 → 그룹 지우고 직접 자식마다 규칙 재적용 |
| 책상 프리팹 루트에 SemanticTag isGroupRoot, 다리에 Rigidbody | 수동 지정이 절대 우선. 다리 그룹 안 생김. 책상 하나 |
| 큐브 하나를 상자로 쓰는데 책상 프리팹 안에 넣어 둠 | 큐브는 책상 그룹의 파츠. 상자로 따로 찾으려면 큐브에 SemanticTag isGroupRoot + overrideNouns |

규칙 3의 임계값 셋과 스톱리스트는 프로젝트 설정으로 뺀다. 윈도우에서 트리를 그대로 보여 줘서 묶음이 어떻게 됐는지 바로 확인할 수 있게 한다. M0 검증의 핵심.

#### 노드 필드
root GameObject, renderers[], bounds, hierarchyPath, GlobalObjectId, nameTokens[], parent, children[], fingerprint

### 3.2 Fingerprint (입력 해시)
```
파츠:  hash( meshGUID + meshDependencyHash,
             materialGUIDs[] + materialDependencyHash[],
             spriteGUID (2D),
             labelerModelVersion )
그룹:  프리팹 루트면  hash( prefabGUID + prefabDependencyHash, labelerModelVersion )
       아니면        hash( 소속 파츠 fingerprint 정렬 + 자식 그룹 fingerprint 정렬, labelerModelVersion )
```
- `AssetDatabase.GetAssetDependencyHash`로 에셋 내용 변경을 잡는다.
- 같은 Mesh+Material 조합, 같은 프리팹은 해시가 같다. 라벨은 1번만 만든다.
- 자식 그룹을 넣는 이유: 직접 파츠가 없는 그룹(자식 프리팹만 거느린 Rigidbody 조상)이 빈 집합 해시로 충돌하는 걸 막는다.

### 3.3 SemanticRecord (SemanticDatabase ScriptableObject에 저장)
```
fingerprint   : Hash128
nouns         : [{ word, confidence }]   top-k
colors        : [{ bucket, ratio }]      red/orange/yellow/green/cyan/blue/purple/pink/white/gray/black/brown
embedding     : float[512]
sourceHint    : 이름 토큰에서 뽑은 명사 (모델 결과와 합침)
labeledAt, modelVersion
```
- 위치: `Assets/SceneGraph/Data/SemanticDatabase.asset`. git으로 팀과 공유.
- 키는 fingerprint. 인스턴스 정보가 아니라 외형 정보다.

### 3.4 SemanticTag (MonoBehaviour, 선택)
렌더러 오브젝트에 붙이는 컴포넌트. 규칙이 틀릴 때 손으로 고치는 용도.
```
overrideNouns     : string[]   같은 큐브를 "상자"/"책상"으로 다르게 쓸 때
excludeFromSearch : bool
isGroupRoot       : bool       여기가 그룹이다. 아래쪽 신호(Rigidbody 등)는 무시한다. 위쪽 신호는 그대로. 절대 우선
splitChildren     : bool       이 오브젝트는 신호로 안 친다. 휴리스틱도 여기서 멈춰 자식들이 따로 묶인다
cachedFingerprint : Hash128    디버그용
```
- 기본은 자동으로 안 붙인다. 씬과 프리팹이 dirty가 되면 팀 작업에 방해된다. 설정에서 켜면 자동 부착.
- 빌드 때 `IProcessSceneWithReport`에서 뗀다.

### 3.5 구조화 쿼리 (LLM ↔ Unity 계약)
```json
{
  "target": {
    "nouns":  ["apple", "fruit"],
    "colors": ["red"],
    "namePattern": null
  },
  "relations": [
    { "type": "on", "object": { "nouns": ["desk", "table"] } }
  ],
  "frame": { "type": "world", "forward": "+z", "up": "+y" },
  "limit": 10
}
```
- `object`는 `target`과 같은 형식. 재귀다. "소파 밑 고양이"의 소파도 쿼리다.
- relation type
  - 축 무관: `on | under | inside | contains | near | above | below`
  - 축 의존: `left_of | right_of | in_front_of | behind`
- `contains`는 target이 object를 포함한다는 뜻. inside의 역방향. 다른 relation은 전부 "target이 object에 대해 어디 있나"라서 "이파리 있는 사과"를 target=leaf, inside apple로 쓰면 이파리가 선택된다. 대신 target apple, relations `[{ "type": "contains", "object": { "nouns": ["leaf"] } }]`로 쓴다.
- `frame`은 축 의존 관계의 기준. 없으면 프로젝트 설정 기본값(월드, forward +z, up +y).
  - `{ "type": "world", "forward": "+z"|"-z"|"+x"|"-x", "up": "+y"|"+z" }`
  - `{ "type": "sceneCamera" }` 씬 뷰 카메라의 forward/right/up
  - `{ "type": "object", "object": { "nouns": ["sofa"] } }` 그 오브젝트 transform의 축. "소파 앞에 있는" 용
- 명사는 영어 소문자. 한국어를 영어 동의어로 펴는 건 LLM이 한다.

**응답**
```json
{
  "results": [
    {
      "globalObjectId": "...",
      "path": "Room/Desk/Apple_01",
      "score": 0.91,
      "matched": {
        "nouns": ["apple"],
        "colors": ["red"],
        "relations": [{ "type": "on", "path": "Room/Desk" }]
      }
    }
  ],
  "indexStatus": { "total": 1240, "pendingLabels": 0 }
}
```

---

## 4. 모듈

| 모듈 | 위치 | 하는 일 |
|---|---|---|
| SceneIndexer | Editor/Index | 열린 씬의 렌더러 스캔, 파츠/그룹 트리 생성, 라이브 bounds |
| Fingerprint | Editor/Index | 3.2 해시 계산 |
| SemanticDatabase | Editor/Data | ScriptableObject, fingerprint → SemanticRecord |
| DirtyTracker | Editor/Index | `ObjectChangeEvents.changesPublished`, `AssetPostprocessor.OnPostprocessAllAssets`, `EditorSceneManager.sceneOpened`를 받아 해시 재비교 후 큐에 넣기 |
| LabelJobQueue | Editor/Labeling | 백그라운드 큐. 프레임당 N개, 진행률 표시, 같은 해시 중복 제거 |
| ThumbnailRenderer | Editor/Labeling | `PreviewRenderUtility`로 노드 서브트리 렌더러 전체(자식 그룹 포함)를 렌더. 1차는 정면 1장, 다각도(3~4장)는 M3. `AssetPreview`는 씬 오브젝트 서브트리를 골라 그리지 못해서 쓰지 않는다 |
| ColorExtractor | Editor/Labeling | 썸네일에서 알파>0 픽셀의 HSV 히스토그램 → 색 버킷 비율 |
| ClipLabeler | Editor/Labeling | Inference Engine으로 CLIP 이미지 인코더 실행 → 임베딩 → 어휘 임베딩과 코사인 → top-k 명사 |
| NameTokenizer | Editor/Labeling | GameObject/Mesh/Material/Prefab 이름을 토큰으로 쪼개 sourceHint 생성 |
| QueryEngine | Editor/Query | 구조화 쿼리 평가, 점수, 정렬, 부모-자식 중복 제거 |
| SpatialRelations | Editor/Query | bounds 기반 관계 판정 |
| SceneGraphWindow | Editor/UI | 쿼리 JSON 입력, 인덱스 트리 보기, 결과 선택/ping, 인덱스 상태, 재라벨링 버튼 |
| EditorHttpServer | Editor/Bridge | `HttpListener` localhost, 메인 스레드로 넘기기 |
| mcp-server | Tools/mcp-server | Node stdio MCP 서버. HTTP로 Unity에 전달 |

### 4.1 공간 관계 (A = target, B = object, 둘 다 bounds)
tol = 0.1 × min(A.size.y, B.size.y). 설정 가능.
- **on**: |A.min.y − B.max.y| ≤ tol, XZ 발자국 겹침 ≥ 0.5 (A 기준)
- **under**: A.max.y ≤ B.max.y, A.center가 B의 XZ 발자국 안, A.center.y < B.center.y
- **inside**: B가 A를 ≥ 0.8 포함
- **contains**: B가 A의 서브트리 안(자식 그룹 또는 소속 파츠)이거나, A가 B를 ≥ 0.8 포함. 둘 중 하나면 성립. 계층 조건을 먼저 보고 bounds는 보조. 조상 제외는 적용하지 않는다. target bounds에서 object를 빼면 안 되기 때문
- **above / below**: XZ 겹침, y 차이 > tol. 접촉 없음
- **near**: 중심 거리 ≤ max(A, B 대각선) × 1.5
- 2D: SpriteRenderer bounds로 같은 함수. `under`/`inside`는 sortingOrder도 본다.

**축 의존 관계** (frame의 right/forward/up 단위 벡터 R, F, U)
- d = A.center − B.center 를 R, F, U에 투영해 (dr, df, du)를 얻는다.
- **left_of**: dr < −tol, |dr|이 셋 중 최대
- **right_of**: dr > tol, |dr|이 최대
- **in_front_of**: df > tol, |df|가 최대
- **behind**: df < −tol, |df|가 최대
- 2D는 frame을 `{ "type": "world", "forward": "+y", "up": "+z" }`로 두면 화면 좌/우/위/아래가 그대로 맞는다.

### 4.2 점수
```
nounScore  = max( 이름 토큰 정확 일치 1.0, 라벨 confidence, 임베딩 유사도 )
colorScore = 요청한 색의 비율 합. 요청 없으면 1.0
relScore   = relation마다 object 후보 중 최고 점수를 곱한 것
total      = nounScore × colorScore × relScore
```
contains도 다른 relation과 같다. object 후보 중 최고 점수를 곱한다. 별도 규칙 없음.
nounScore < 0.3이면 탈락. 부모와 자식이 같은 명사로 걸리면 부모만 남긴다. 상위 limit개 반환.

### 4.3 MCP 툴
- `scene_query(query)` → 결과 목록
- `scene_select(globalObjectIds[])` → Selection 설정 + ping
- `scene_describe(globalObjectId)` → 이름, 경로, bounds, 라벨, 색, 머티리얼
- `scene_index_status()` → 총 개수, 대기 중 라벨, 마지막 갱신
- 툴 설명에 쿼리 스키마, frame 기본값, "한국어 명사는 영어 동의어로 펴라", "포함 관계('~가 있는', '~가 달린')는 contains"를 적는다.

---

## 5. 마일스톤

### M0. 뼈대와 라이브 인덱스
- 폴더, asmdef, `SceneIndexer`, 파츠/그룹 트리, `NameTokenizer`
- `SceneGraphWindow`에 트리와 bounds 표시
- **검증**: 테스트 씬(책상, 사과 2개(이파리 달린 빨강 / 이파리 없는 초록), 소파, 고양이, 상자, 부품 FBX 조립 책상, 선반과 Rigidbody 책)에서 묶음과 bounds가 맞게 나온다. 3.1 예시 표의 열두 경우가 표대로 묶인다.

### M1. 쿼리 엔진 (ML 없음)
- `QueryEngine`, `SpatialRelations`, `ThumbnailRenderer` 1장 버전, `ColorExtractor`
- `frame` 해석(world / sceneCamera / object), 축 의존 관계 4종
- 윈도우에서 JSON 입력 → 결과 선택/ping
- EditMode 테스트: 관계 11종, frame 변환, 점수, 조상 제외 bounds
- **검증**: `{"nouns":["apple"],"colors":["red"],"relations":[{"type":"on","object":{"nouns":["desk"]}}]}`가 빨간 사과 하나만 고른다.
- **검증**: Apple/{Body, Leaf}와 Apple/{Body} 두 사과가 있을 때 `{"target":{"nouns":["apple"]},"relations":[{"type":"contains","object":{"nouns":["leaf"]}}]}`가 이파리 있는 사과 하나만 고른다.

### M2. 미리 만들기와 무효화
- `Fingerprint`, `SemanticDatabase`, `SemanticTag`, `DirtyTracker`, `LabelJobQueue`
- 색 추출 결과를 DB에 저장, 이름 토큰을 sourceHint로 저장
- **검증**: 머티리얼 색을 빨강에서 파랑으로 바꾸면 그 사과가 `colors:["red"]` 결과에서 빠진다. 오브젝트를 옮기면 `on` 판정이 바로 바뀐다. 에디터를 다시 켜도 DB가 남아 있다.

### M3. 로컬 모델 라벨링
- `com.unity.ai.inference` 추가, CLIP 이미지 인코더 ONNX (ViT-B/32 fp16 또는 MobileCLIP)
- 어휘 임베딩은 Python으로 미리 계산해 에셋으로 넣는다. Unity에 텍스트 인코더와 토크나이저가 필요 없어진다.
- `ThumbnailRenderer` 다각도, `ClipLabeler`, sourceHint와 합치기
- **논의 항목(결정 보류)**: 텍스트 인코더 도입 여부. 이파리가 한 메시에 들어 있는 사과처럼 어휘 단일 명사로 표현 안 되는 묘사("an apple with a leaf")는 텍스트 인코더 없이는 이름 토큰이나 SemanticTag에 기댄다. Node MCP 서버 쪽에서 텍스트 인코더를 돌려 쿼리에 임베딩을 싣는 방안 포함.
- **검증**: 이름이 "Cube (3)"인 사과 메시가 `nouns:["apple"]`로 나온다. 오브젝트 1,000개 첫 라벨링 중 에디터가 멈추지 않는다.

### M4. MCP 브리지
- `EditorHttpServer`, `Tools/mcp-server` (Node/TypeScript), 툴 4개
- Claude Code 설정 예시(.mcp.json) 문서
- **검증**: Claude Code에서 "책상 위에 있는 빨간 사과 찾아줘"라고 하면 하이어라키에서 그 오브젝트가 선택된다.

### M5. 마무리
- 2D 스프라이트, 빌드 때 `SemanticTag` 제거, 설정(톨러런스, 묶기 임계값, 스톱리스트, 모델 경로, 자동 부착)
- 결과 하이어라키 하이라이트, 어휘 확장 방법, README

---

## 6. 결정과 리스크

| 항목 | 결정 | 이유 / 리스크 |
|---|---|---|
| 캐시 키 | 인스턴스가 아니라 fingerprint(외형) | 같은 외형 N개는 1번만 계산. 인스턴스 예외는 SemanticTag |
| 위치 정보 | 저장 안 함 | 옮겨도 안 깨진다. 스캔은 ms 단위 |
| 무효화 | 이벤트는 트리거, 정합성은 해시 | 이벤트를 놓쳐도(에디터 꺼진 채 git pull) 해시 비교로 잡힌다 |
| 묶기 | 경로 위 신호 조상 전부를 그룹으로, 각 신호 그룹 안에서는 휴리스틱으로 하위 묶음 | Model 프리팹은 신호에서 뺀다. 플레인 메시 씬은 틀릴 수 있다. 파츠를 살려 두고 SemanticTag로 고친다 |
| 모델 | CLIP 계열, Inference Engine으로 에디터 안에서 실행 | 임베딩으로 상위어 검색이 공짜. 모델 파일(~150MB)은 Git LFS 또는 다운로드 스크립트 |
| 어휘 | 영어 명사 수천 개 + 임베딩 사전 계산 | 한국어는 LLM이 번역. 어휘에 없는 명사는 이름 토큰에 기댄다 |
| 썸네일 | PreviewRenderUtility. 1차 1장, M3에서 다각도 | 스킨드 메시, 거대 오브젝트, 투명 재질은 라벨 품질이 떨어질 수 있다 |
| 컴포넌트 | 기본 미부착 | 자동 부착은 씬/프리팹을 dirty로 만든다 |
| CLI | 안 쓴다 | batchmode는 호출마다 수십 초, 열린 에디터와 같이 못 쓴다 |
| 축 의존 관계 | `frame` 옵션 | 기본 월드 축. 씬 카메라/오브젝트 프레임은 명시할 때만. LLM이 "앞"을 어느 프레임으로 볼지 헷갈릴 수 있어 툴 설명에 기본값을 적는다 |

---

## 7. 폴더 구조

```
Assets/SceneGraph/
  Runtime/
    SceneGraph.Runtime.asmdef
    SemanticTag.cs
  Editor/
    SceneGraph.Editor.asmdef
    Index/     SceneIndexer.cs  IndexNode.cs  GroupingRules.cs  Fingerprint.cs  DirtyTracker.cs  NameTokenizer.cs
    Data/      SemanticDatabase.cs  SemanticRecord.cs
    Labeling/  LabelJobQueue.cs  ThumbnailRenderer.cs  ColorExtractor.cs  ClipLabeler.cs
    Query/     QuerySchema.cs  QueryEngine.cs  SpatialRelations.cs
    UI/        SceneGraphWindow.cs
    Bridge/    EditorHttpServer.cs  McpHandlers.cs
    Build/     StripSemanticTags.cs
  Data/
    SemanticDatabase.asset
    Vocab/ nouns.txt  nouns_embeddings.bytes
  Models/
    clip_image_encoder.onnx
  Tests/Editor/
    GroupingRulesTests.cs  SpatialRelationsTests.cs  QueryEngineTests.cs  FingerprintTests.cs
Tools/
  mcp-server/   package.json  src/index.ts
  vocab/        build_vocab_embeddings.py
```
