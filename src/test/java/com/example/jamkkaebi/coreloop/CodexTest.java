package com.example.jamkkaebi.coreloop;

import com.example.jamkkaebi.friend.domain.Friendship;
import com.example.jamkkaebi.friend.repository.FriendshipRepository;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.UserBuilding;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.repository.RelicMasterRepository;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.request.MockMvcRequestBuilders;

import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.LocalDateTime;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 도감 — 시대별 진행률, 카드 상태 3종, 완료한 카드에만 열리는 실화 기록.
 *
 * <p>도깨비 번호는 시대 순서대로 1~3 삼국, 4~6 고려, 7~9 조선이다.
 */
class CodexTest extends CoreLoopApiTestSupport {

    private static final int GEUMDONG_YEONGA = 1;  // 금동연가7년명여래입상 — 삼국
    private static final int INCENSE_BURNER = 2;   // 금동대향로 — 삼국
    private static final int GEUMGWAN = 3;         // 금관총 금관 — 삼국
    private static final int MAPAE = 8;            // 마패 — 조선

    @Autowired
    private RelicMasterRepository relicMasterRepository;
    @Autowired
    private FriendshipRepository friendshipRepository;

    @AfterEach
    void clearFriendships() {
        friendshipRepository.deleteAll();
    }

    @Test
    @DisplayName("아무것도 없으면 세 시대가 삼국 → 고려 → 조선 순서로 전부 0 이다")
    void 빈_도감() throws Exception {
        Player player = player("신입");

        get(player, "/api/codex")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("OK"))
                .andExpect(jsonPath("$.data.eras.length()").value(3))
                .andExpect(jsonPath("$.data.eras[0].era").value("THREE_KINGDOMS"))
                .andExpect(jsonPath("$.data.eras[1].era").value("GORYEO"))
                .andExpect(jsonPath("$.data.eras[2].era").value("JOSEON"))
                .andExpect(jsonPath("$.data.eras[0].completedCount").value(0))
                .andExpect(jsonPath("$.data.eras[0].acquiredCount").value(0))
                .andExpect(jsonPath("$.data.eras[0].buildingAcquired").value(false))
                .andExpect(jsonPath("$.data.completedRelics").value(0))
                .andExpect(jsonPath("$.data.collectionRate").value(0))
                .andExpect(jsonPath("$.data.buildingsAcquired").value(0));
    }

    @Test
    @DisplayName("시대별 완료·획득 수와 건물 보유, 전체 수집률(내림)을 집계한다")
    void 요약_집계() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);   // 삼국: 완료 1
        give(player, GEUMGWAN);                  // 삼국: 진행 중 1
        for (int relicId : relicIdsOf(Era.GORYEO)) {
            giveCompleted(player, relicId);   // 고려: 완료 3
        }
        giveBuilding(player, Era.GORYEO);
        giveCompleted(player, MAPAE);            // 조선: 완료 1

        get(player, "/api/codex")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.eras[0].completedCount").value(1))
                .andExpect(jsonPath("$.data.eras[0].acquiredCount").value(2))
                .andExpect(jsonPath("$.data.eras[0].buildingAcquired").value(false))
                .andExpect(jsonPath("$.data.eras[1].completedCount").value(3))
                .andExpect(jsonPath("$.data.eras[1].acquiredCount").value(3))
                .andExpect(jsonPath("$.data.eras[1].buildingAcquired").value(true))
                .andExpect(jsonPath("$.data.eras[2].completedCount").value(1))
                .andExpect(jsonPath("$.data.eras[2].acquiredCount").value(1))
                .andExpect(jsonPath("$.data.completedRelics").value(5))
                // 5 ÷ 9 = 55.5…% 를 내린다.
                .andExpect(jsonPath("$.data.collectionRate").value(55))
                .andExpect(jsonPath("$.data.buildingsAcquired").value(1));
    }

    @Test
    @DisplayName("수집률은 유물만으로 계산한다 — 건물만 있으면 0%, 유물 9종을 다 모으면 100%")
    void 수집률은_유물만으로() throws Exception {
        Player player = player("학예사");
        giveBuilding(player, Era.THREE_KINGDOMS);
        giveBuilding(player, Era.GORYEO);
        giveBuilding(player, Era.JOSEON);

        get(player, "/api/codex")
                .andExpect(jsonPath("$.data.collectionRate").value(0))
                .andExpect(jsonPath("$.data.buildingsAcquired").value(3));

        for (int relicId = 1; relicId <= RelicMaster.TOTAL; relicId++) {
            giveCompleted(player, relicId);
        }
        get(player, "/api/codex")
                .andExpect(jsonPath("$.data.completedRelics").value(9))
                .andExpect(jsonPath("$.data.collectionRate").value(100));
    }

    @Test
    @DisplayName("진행 중인 유물은 획득으로만 세고, 완료로는 세지 않는다")
    void 진행_중은_획득만() throws Exception {
        Player player = player("학예사");
        give(player, GEUMDONG_YEONGA);

        get(player, "/api/codex")
                .andExpect(jsonPath("$.data.eras[0].acquiredCount").value(1))
                .andExpect(jsonPath("$.data.eras[0].completedCount").value(0))
                .andExpect(jsonPath("$.data.completedRelics").value(0))
                .andExpect(jsonPath("$.data.collectionRate").value(0));
    }

    @Test
    @DisplayName("한 번 완료한 유물은 재봉인돼도 도감에서 풀 컬러로 남고 수집률도 그대로다")
    void 재봉인돼도_풀_컬러() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);

        clock.advance(Duration.ofDays(20));

        get(player, "/api/codex")
                .andExpect(jsonPath("$.data.eras[0].completedCount").value(1))
                .andExpect(jsonPath("$.data.collectionRate").value(11));
        get(player, "/api/codex/eras/{era}", "THREE_KINGDOMS")
                .andExpect(jsonPath("$.data.cards[1].cardState").value("FULL_COLOR"))
                // 선명도는 카드 상태가 아니라 배지로 따로 보여 준다.
                .andExpect(jsonPath("$.data.cards[1].clarity").value("RESEALED"))
                .andExpect(jsonPath("$.data.cards[1].awakeningStage").value("AWAKENED"));
        get(player, "/api/codex/relics/{id}", INCENSE_BURNER)
                .andExpect(jsonPath("$.data.cardState").value("FULL_COLOR"))
                .andExpect(jsonPath("$.data.record.story").isNotEmpty());
    }

    @Test
    @DisplayName("시대 상세는 카드 3장을 번호 순으로, 상태에 맞는 만큼만 담는다")
    void 시대_상세() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);   // 풀 컬러
        give(player, GEUMGWAN);                  // 실루엣
        // GEUMDONG_YEONGA 는 미획득

        get(player, "/api/codex/eras/{era}", "THREE_KINGDOMS")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("OK"))
                .andExpect(jsonPath("$.data.era").value("THREE_KINGDOMS"))
                // 1 ÷ 3 = 33.3…% 를 내린다.
                .andExpect(jsonPath("$.data.collectionRate").value(33))
                .andExpect(jsonPath("$.data.cards.length()").value(3))
                // 미획득 — 자리만 차지한다.
                .andExpect(jsonPath("$.data.cards[0].relicId").value(GEUMDONG_YEONGA))
                .andExpect(jsonPath("$.data.cards[0].cardState").value("LOCKED"))
                .andExpect(jsonPath("$.data.cards[0].relic").doesNotExist())
                .andExpect(jsonPath("$.data.cards[0].awakeningStage").doesNotExist())
                .andExpect(jsonPath("$.data.cards[0].clarity").doesNotExist())
                // 풀 컬러 — 이름이 전부 공개되고 배지가 붙는다.
                .andExpect(jsonPath("$.data.cards[1].relicId").value(INCENSE_BURNER))
                .andExpect(jsonPath("$.data.cards[1].cardState").value("FULL_COLOR"))
                .andExpect(jsonPath("$.data.cards[1].relic.revealLevel").value("NAMED"))
                .andExpect(jsonPath("$.data.cards[1].relic.poeticAlias").value("백제, 향이 스민 산맥"))
                .andExpect(jsonPath("$.data.cards[1].relic.realName").value("금동대향로"))
                .andExpect(jsonPath("$.data.cards[1].relic.spiritName").value("향산이"))
                .andExpect(jsonPath("$.data.cards[1].awakeningStage").value("AWAKENED"))
                .andExpect(jsonPath("$.data.cards[1].clarity").value("CLEAR"))
                // 실루엣 — 작업대 진행 중이라 이름이 없고 배지도 없다.
                .andExpect(jsonPath("$.data.cards[2].relicId").value(GEUMGWAN))
                .andExpect(jsonPath("$.data.cards[2].cardState").value("SILHOUETTE"))
                .andExpect(jsonPath("$.data.cards[2].relic.revealLevel").value("SILHOUETTE"))
                .andExpect(jsonPath("$.data.cards[2].relic.poeticAlias").doesNotExist())
                .andExpect(jsonPath("$.data.cards[2].relic.realName").doesNotExist())
                .andExpect(jsonPath("$.data.cards[2].awakeningStage").doesNotExist())
                .andExpect(jsonPath("$.data.cards[2].clarity").doesNotExist());
    }

    @Test
    @DisplayName("진행 중인 카드는 작업대와 같은 단계까지만 공개한다")
    void 진행_중_카드는_단계적_공개() throws Exception {
        Player player = player("학예사");
        UserRelic relic = give(player, GEUMGWAN);
        relic.clearFirstPhase(LocalDateTime.now(clock));   // 봉인 해제 클리어 → 별칭 공개
        userRelicRepository.save(relic);

        get(player, "/api/codex/eras/{era}", "THREE_KINGDOMS")
                .andExpect(jsonPath("$.data.cards[2].cardState").value("SILHOUETTE"))
                .andExpect(jsonPath("$.data.cards[2].relic.revealLevel").value("ALIAS"))
                .andExpect(jsonPath("$.data.cards[2].relic.poeticAlias").value("신라, 머리에 인 금빛 숲"))
                .andExpect(jsonPath("$.data.cards[2].relic.realName").doesNotExist())
                .andExpect(jsonPath("$.data.cards[2].relic.spiritName").doesNotExist());
    }

    @Test
    @DisplayName("건물 슬롯은 미보유면 번호와 상태만, 보유하면 이름과 레벨을 싣는다")
    void 건물_슬롯() throws Exception {
        Player player = player("학예사");
        int buildingId = catalog.buildingOf(Era.GORYEO).orElseThrow().getId();
        String buildingName = catalog.buildingOf(Era.GORYEO).orElseThrow().getName();

        get(player, "/api/codex/eras/{era}", "GORYEO")
                .andExpect(jsonPath("$.data.building.buildingId").value(buildingId))
                .andExpect(jsonPath("$.data.building.acquired").value(false))
                .andExpect(jsonPath("$.data.building.name").doesNotExist())
                .andExpect(jsonPath("$.data.building.level").doesNotExist());

        UserBuilding building = giveBuilding(player, Era.GORYEO);
        building.levelUp();
        userBuildingRepository.save(building);

        get(player, "/api/codex/eras/{era}", "GORYEO")
                .andExpect(jsonPath("$.data.building.buildingId").value(buildingId))
                .andExpect(jsonPath("$.data.building.acquired").value(true))
                .andExpect(jsonPath("$.data.building.name").value(buildingName))
                .andExpect(jsonPath("$.data.building.level").value(2));
        // 다른 시대의 슬롯은 영향받지 않는다.
        get(player, "/api/codex/eras/{era}", "JOSEON")
                .andExpect(jsonPath("$.data.building.acquired").value(false));
    }

    @Test
    @DisplayName("시대 이름이 열거 값이 아니면 C001 이다")
    void 잘못된_시대() throws Exception {
        Player player = player("학예사");

        get(player, "/api/codex/eras/{era}", "ATLANTIS")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
        get(player, "/api/codex/eras/{era}", "goryeo")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
    }

    @Test
    @DisplayName("카드 상세 — 완료한 카드에서만 실화 기록이 열린다")
    void 카드_상세_완료() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        String story = catalog.relic(INCENSE_BURNER).getRealStory();

        get(player, "/api/codex/relics/{id}", INCENSE_BURNER)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("OK"))
                .andExpect(jsonPath("$.data.relicId").value(INCENSE_BURNER))
                .andExpect(jsonPath("$.data.cardState").value("FULL_COLOR"))
                .andExpect(jsonPath("$.data.relic.revealLevel").value("NAMED"))
                .andExpect(jsonPath("$.data.relic.realName").value("금동대향로"))
                .andExpect(jsonPath("$.data.awakeningStage").value("AWAKENED"))
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.record.story").value(story))
                // 링크는 콘텐츠가 정해지기 전이라 비어 있고, 비어 있으면 키가 빠진다.
                .andExpect(jsonPath("$.data.record.heritageUrl").doesNotExist())
                .andExpect(jsonPath("$.data.record.reportUrl").doesNotExist());
    }

    @Test
    @DisplayName("카드 상세 — 미획득은 200 으로 가려진 카드를 주고, 실화 기록이 새지 않는다")
    void 카드_상세_미획득() throws Exception {
        Player player = player("학예사");

        String body = get(player, "/api/codex/relics/{id}", INCENSE_BURNER)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.relicId").value(INCENSE_BURNER))
                .andExpect(jsonPath("$.data.cardState").value("LOCKED"))
                .andExpect(jsonPath("$.data.relic").doesNotExist())
                .andExpect(jsonPath("$.data.awakeningStage").doesNotExist())
                .andExpect(jsonPath("$.data.clarity").doesNotExist())
                .andExpect(jsonPath("$.data.record").doesNotExist())
                .andReturn().getResponse().getContentAsString(StandardCharsets.UTF_8);

        assertNoSecrets(body, INCENSE_BURNER);
    }

    @Test
    @DisplayName("카드 상세 — 진행 중인 카드도 실화 기록과 이름을 내지 않는다")
    void 카드_상세_진행_중() throws Exception {
        Player player = player("학예사");
        give(player, INCENSE_BURNER);

        String body = get(player, "/api/codex/relics/{id}", INCENSE_BURNER)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.cardState").value("SILHOUETTE"))
                .andExpect(jsonPath("$.data.relic.revealLevel").value("SILHOUETTE"))
                .andExpect(jsonPath("$.data.awakeningStage").doesNotExist())
                .andExpect(jsonPath("$.data.clarity").doesNotExist())
                .andExpect(jsonPath("$.data.record").doesNotExist())
                .andReturn().getResponse().getContentAsString(StandardCharsets.UTF_8);

        assertNoSecrets(body, INCENSE_BURNER);
    }

    @Test
    @DisplayName("링크가 채워지면 완료한 카드에만 실리고, 완료 전에는 채워져 있어도 나가지 않는다")
    void 링크는_완료한_카드에만() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        give(player, GEUMGWAN);
        setLinks(INCENSE_BURNER, "https://heritage.example/incense", "https://report.example/incense");
        setLinks(GEUMGWAN, "https://heritage.example/geumgwan", "https://report.example/geumgwan");
        try {
            get(player, "/api/codex/relics/{id}", INCENSE_BURNER)
                    .andExpect(jsonPath("$.data.record.heritageUrl").value("https://heritage.example/incense"))
                    .andExpect(jsonPath("$.data.record.reportUrl").value("https://report.example/incense"));

            String body = get(player, "/api/codex/relics/{id}", GEUMGWAN)
                    .andExpect(jsonPath("$.data.record").doesNotExist())
                    .andReturn().getResponse().getContentAsString(StandardCharsets.UTF_8);
            assertThat(body).doesNotContain("example");
        } finally {
            setLinks(INCENSE_BURNER, null, null);
            setLinks(GEUMGWAN, null, null);
        }
    }

    @Test
    @DisplayName("실제 정화로 시대를 채우면 도감이 이어서 바뀐다 — 2종까지는 건물이 없고, 3종째에 건물이 생긴다")
    void 정화_흐름과_이어진다() throws Exception {
        Player player = player("학예사");
        int[] goryeo = relicIdsOf(Era.GORYEO).stream().mapToInt(Integer::intValue).toArray();
        for (int relicId : goryeo) {
            give(player, relicId);
        }
        completeFirstPurification(player, goryeo[0]);
        completeFirstPurification(player, goryeo[1]);
        clearPhase(player, goryeo[2], null);   // 봉인 해제
        clearPhase(player, goryeo[2], null);   // 기억 회복 — 아직 정령 수호가 남았다

        get(player, "/api/codex")
                .andExpect(jsonPath("$.data.eras[1].completedCount").value(2))
                .andExpect(jsonPath("$.data.eras[1].acquiredCount").value(3))
                .andExpect(jsonPath("$.data.eras[1].buildingAcquired").value(false))
                .andExpect(jsonPath("$.data.collectionRate").value(22))
                .andExpect(jsonPath("$.data.buildingsAcquired").value(0));
        get(player, "/api/codex/eras/{era}", "GORYEO")
                .andExpect(jsonPath("$.data.cards[0].cardState").value("FULL_COLOR"))
                .andExpect(jsonPath("$.data.cards[1].cardState").value("FULL_COLOR"))
                // 기억 회복까지 마친 카드는 별칭은 보이고 이름은 아직 가려져 있다.
                .andExpect(jsonPath("$.data.cards[2].cardState").value("SILHOUETTE"))
                .andExpect(jsonPath("$.data.cards[2].relic.revealLevel").value("FORM"))
                .andExpect(jsonPath("$.data.cards[2].relic.poeticAlias").isNotEmpty())
                .andExpect(jsonPath("$.data.cards[2].relic.realName").doesNotExist())
                .andExpect(jsonPath("$.data.building.acquired").value(false));

        clearPhase(player, goryeo[2], null);   // 정령 수호 — 시대 완성

        get(player, "/api/codex")
                .andExpect(jsonPath("$.data.eras[1].completedCount").value(3))
                .andExpect(jsonPath("$.data.eras[1].buildingAcquired").value(true))
                .andExpect(jsonPath("$.data.collectionRate").value(33))
                .andExpect(jsonPath("$.data.buildingsAcquired").value(1));
        get(player, "/api/codex/eras/{era}", "GORYEO")
                .andExpect(jsonPath("$.data.collectionRate").value(100))
                .andExpect(jsonPath("$.data.cards[2].cardState").value("FULL_COLOR"))
                .andExpect(jsonPath("$.data.cards[2].relic.realName").isNotEmpty())
                .andExpect(jsonPath("$.data.cards[2].awakeningStage").value("AWAKENED"))
                .andExpect(jsonPath("$.data.cards[2].clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.building.acquired").value(true))
                .andExpect(jsonPath("$.data.building.level").value(1))
                .andExpect(jsonPath("$.data.building.name").isNotEmpty());
    }

    @Test
    @DisplayName("사용자가 지어 준 도깨비 이름이 도감에도 그대로 나온다")
    void 지어_준_이름() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        patch(player, "/api/relics/{id}/spirit-name", "{\"name\":\"향로지기\"}", INCENSE_BURNER)
                .andExpect(status().isOk());

        get(player, "/api/codex/eras/{era}", "THREE_KINGDOMS")
                .andExpect(jsonPath("$.data.cards[1].relic.spiritName").value("향로지기"));
        get(player, "/api/codex/relics/{id}", INCENSE_BURNER)
                .andExpect(jsonPath("$.data.relic.spiritName").value("향로지기"));
    }

    @Test
    @DisplayName("도감의 수집률은 친구 목록에서 친구가 보는 내 수집률과 같다")
    void 친구_카드_수집률과_같다() throws Exception {
        Player owner = player("주인");
        Player visitor = player("방문객");
        friendshipRepository.save(Friendship.between(owner.id(), visitor.id()));
        giveCompleted(owner, INCENSE_BURNER);
        giveCompleted(owner, MAPAE);
        give(owner, GEUMGWAN);   // 진행 중은 세지 않는다

        int rate = read(get(owner, "/api/codex"), "$.data.collectionRate");
        assertThat(rate).isEqualTo(22);
        get(visitor, "/api/friends")
                .andExpect(jsonPath("$.data.friends[0].collectionRate").value(rate));
    }

    @Test
    @DisplayName("없는 유물 번호는 C001 이다")
    void 없는_유물() throws Exception {
        Player player = player("학예사");

        get(player, "/api/codex/relics/{id}", 999)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"))
                .andExpect(jsonPath("$.errors[0].field").value("relicId"));
        get(player, "/api/codex/relics/{id}", 0)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
        get(player, "/api/codex/relics/{id}", "abc")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
    }

    @Test
    @DisplayName("남의 유물은 내 도감에 잡히지 않는다")
    void 사용자_격리() throws Exception {
        Player player = player("학예사");
        Player other = player("이웃");
        giveCompleted(other, INCENSE_BURNER);
        giveBuilding(other, Era.THREE_KINGDOMS);

        get(player, "/api/codex")
                .andExpect(jsonPath("$.data.completedRelics").value(0))
                .andExpect(jsonPath("$.data.buildingsAcquired").value(0));
        get(player, "/api/codex/eras/{era}", "THREE_KINGDOMS")
                .andExpect(jsonPath("$.data.cards[1].cardState").value("LOCKED"))
                .andExpect(jsonPath("$.data.building.acquired").value(false));
        get(player, "/api/codex/relics/{id}", INCENSE_BURNER)
                .andExpect(jsonPath("$.data.cardState").value("LOCKED"));
    }

    @Test
    @DisplayName("도감은 열람 전용이라 조회 말고는 받지 않는다")
    void 열람_전용() throws Exception {
        Player player = player("학예사");

        post(player, "/api/codex", "{}")
                .andExpect(status().isMethodNotAllowed());
        put(player, "/api/codex/relics/2", "{}")
                .andExpect(status().isMethodNotAllowed());
        patch(player, "/api/codex/relics/2", "{}")
                .andExpect(status().isMethodNotAllowed());
    }

    @Test
    @DisplayName("인증 없이는 도감을 볼 수 없다")
    void 인증_필요() throws Exception {
        for (String url : new String[]{"/api/codex", "/api/codex/eras/GORYEO", "/api/codex/relics/2"}) {
            mockMvc.perform(MockMvcRequestBuilders.get(url).contentType(MediaType.APPLICATION_JSON))
                    .andExpect(status().isUnauthorized());
        }
    }

    /** 가려진 카드의 응답에 이름·실화 텍스트가 한 글자도 실리지 않았는지 본다. */
    private void assertNoSecrets(String body, int relicId) {
        RelicMaster master = catalog.relic(relicId);
        assertThat(body)
                .doesNotContain(master.getRealName())
                .doesNotContain(master.getPoeticAlias())
                .doesNotContain(master.getSpiritDefaultName())
                .doesNotContain(master.getRealStory())
                .doesNotContain("story");
    }

    private UserBuilding giveBuilding(Player player, Era era) {
        int buildingId = catalog.buildingOf(era).orElseThrow().getId();
        return userBuildingRepository.save(
                UserBuilding.awarded(player.id(), buildingId, LocalDateTime.now(clock)));
    }

    /** 링크는 시딩이 비워 두는 값이라 테스트가 직접 채운다. 시딩은 덮어쓰기라 다음 테스트에서 비워지지만 바로 되돌린다. */
    private void setLinks(int relicId, String heritageUrl, String reportUrl) {
        transactionTemplate.executeWithoutResult(status -> {
            RelicMaster master = relicMasterRepository.findById(relicId).orElseThrow();
            master.refresh(master.getSealMedium(), master.getPoeticAlias(), master.getRealName(),
                    master.getSpiritDefaultName(), master.getRealStory(), heritageUrl, reportUrl,
                    master.getDisplayOrder());
        });
    }
}
