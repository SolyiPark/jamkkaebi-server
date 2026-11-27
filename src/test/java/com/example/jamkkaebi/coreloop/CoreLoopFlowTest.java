package com.example.jamkkaebi.coreloop;

import com.example.jamkkaebi.relic.domain.Difficulty;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.RelicStatus;
import com.example.jamkkaebi.relic.domain.UserRelic;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.LocalDateTime;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.hamcrest.Matchers.startsWith;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 코어 루프 완주 — 상자에서 뽑아 3페이즈를 마치고, 관리 플레이로 선명도를 지키기까지.
 *
 * <p>백엔드 계획 M1 의 완료 판정이 그대로 이 파일의 첫 테스트다.
 */
class CoreLoopFlowTest extends CoreLoopApiTestSupport {

    @Test
    @DisplayName("상자를 열어 얻은 유물을 3페이즈까지 마치면 완료 상태가 되고 이름이 공개된다")
    void 상자에서_뽑아_오염_관리까지() throws Exception {
        Player player = player("잠꾸러기");

        var opened = post(player, "/api/box/open", """
                {"source":"FREE","requestId":"3f1c9a52-6a1e-4c1b-9d0e-2b7f0c8a4e11"}
                """)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("BOX_OPENED"))
                .andExpect(jsonPath("$.data.result").value("NEW"))
                // 획득 직후는 실루엣이지만 시적 별칭은 처음부터 보인다.
                .andExpect(jsonPath("$.data.relic.revealLevel").value("SILHOUETTE"))
                .andExpect(jsonPath("$.data.relic.poeticAlias").isNotEmpty())
                .andExpect(jsonPath("$.data.relic.realName").doesNotExist())
                .andExpect(jsonPath("$.data.relic.spiritName").doesNotExist());
        int relicId = read(opened, "$.data.relic.relicId");

        // 1페이즈 — 봉인 해제. 형태가 열린다.
        clearPhase(player, relicId, null)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.revealed").value("FORM"))
                .andExpect(jsonPath("$.data.next.action").value("NEXT_PHASE"))
                .andExpect(jsonPath("$.data.next.phase").value("RECALL"))
                .andExpect(jsonPath("$.data.rewards.growthGauge").doesNotExist())
                // 최초 정화는 돌봄 횟수를 세지 않는다.
                .andExpect(jsonPath("$.data.careCount").doesNotExist());

        // 2페이즈 — 기억 회복. 실제 이름이 열리지만 도깨비 이름은 아직이다.
        clearPhase(player, relicId, null)
                .andExpect(jsonPath("$.data.revealed").value("NAMED"))
                .andExpect(jsonPath("$.data.relic.realName").isNotEmpty())
                .andExpect(jsonPath("$.data.relic.spiritName").doesNotExist())
                .andExpect(jsonPath("$.data.next.phase").value("GUARD"));

        // 3페이즈 — 오염 관리. 완료하면 도깨비 이름까지 공개된다.
        clearPhase(player, relicId, null)
                .andExpect(jsonPath("$.data.status").value("COMPLETED"))
                // 이름은 기억 회복에서 이미 공개됐으므로 새로 오른 공개 단계는 없다.
                .andExpect(jsonPath("$.data.revealed").doesNotExist())
                .andExpect(jsonPath("$.data.relic.realName").isNotEmpty())
                .andExpect(jsonPath("$.data.relic.spiritName").isNotEmpty())
                .andExpect(jsonPath("$.data.next.action").value("DONE"))
                .andExpect(jsonPath("$.data.awakening").doesNotExist())
                .andExpect(jsonPath("$.data.careCount").value(0))
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"));

        UserRelic stored = userRelicRepository
                .findByUserIdAndRelicId(player.id(), relicId).orElseThrow();
        assertThat(stored.getStatus()).isEqualTo(RelicStatus.COMPLETED);
        assertThat(stored.getCareCount()).isZero();
        assertThat(stored.getLastPurifiedAt()).isNotNull();
    }

    @Test
    @DisplayName("한 시대의 유물 3종을 모두 마치면 건물이 자동으로 지급된다")
    void 시대_완성_건물_지급() throws Exception {
        Player player = player("수집가");
        List<Integer> goryeo = relicIdsOf(Era.GORYEO);

        completeFirstPurification(player, giveAndReturnId(player, goryeo.get(0)));
        completeFirstPurification(player, giveAndReturnId(player, goryeo.get(1)));
        assertThat(userBuildingRepository.findAllByUserId(player.id())).isEmpty();

        completeFirstPurification(player, giveAndReturnId(player, goryeo.get(2)))
                .andExpect(jsonPath("$.data.buildingAwarded.era").value("GORYEO"))
                .andExpect(jsonPath("$.data.buildingAwarded.level").value(1));

        assertThat(userBuildingRepository.findAllByUserId(player.id())).hasSize(1);
    }

    @Test
    @DisplayName("최초 정화는 보통 하나만 고를 수 있고, 다른 난이도는 P001")
    void 최초_정화_난이도_고정() throws Exception {
        Player player = player("새내기");
        int relicId = giveAndReturnId(player, 1);

        get(player, "/api/relics/{id}", relicId)
                .andExpect(jsonPath("$.data.nextPlay.entryRoute").value("FIRST"))
                .andExpect(jsonPath("$.data.nextPlay.difficulties.length()").value(1))
                .andExpect(jsonPath("$.data.nextPlay.difficulties[0]").value("NORMAL"))
                .andExpect(jsonPath("$.data.nextPlay.difficultySelectable").doesNotExist())
                .andExpect(jsonPath("$.data.careCount").doesNotExist());

        post(player, "/api/purifications", """
                {"relicId":%d,"difficulty":"HARD"}
                """.formatted(relicId))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("P001"));

        // 하나뿐인 난이도는 보내도 되고 생략해도 된다.
        post(player, "/api/purifications", """
                {"relicId":%d,"difficulty":"NORMAL"}
                """.formatted(relicId))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.data.difficulty").value("NORMAL"));
    }

    @Test
    @DisplayName("선명하면 쉬움·보통·어려움, 흐려지면 쉬움이 빠진다")
    void 선명도별_고를_수_있는_난이도() throws Exception {
        Player player = player("돌보미");
        int relicId = completedRelic(player);

        get(player, "/api/relics/{id}", relicId)
                .andExpect(jsonPath("$.data.nextPlay.entryRoute").value("CARE"))
                .andExpect(jsonPath("$.data.nextPlay.remainingPhases[0]").value("GUARD"))
                .andExpect(jsonPath("$.data.nextPlay.difficulties[0]").value("EASY"))
                .andExpect(jsonPath("$.data.nextPlay.difficulties[1]").value("NORMAL"))
                .andExpect(jsonPath("$.data.nextPlay.difficulties[2]").value("HARD"));

        clock.advance(Duration.ofDays(8));
        get(player, "/api/relics/{id}", relicId)
                .andExpect(jsonPath("$.data.clarity").value("FADED"))
                .andExpect(jsonPath("$.data.nextPlay.difficulties.length()").value(2))
                .andExpect(jsonPath("$.data.nextPlay.difficulties[0]").value("NORMAL"))
                .andExpect(jsonPath("$.data.nextPlay.difficulties[1]").value("HARD"));

        post(player, "/api/purifications", """
                {"relicId":%d,"difficulty":"EASY"}
                """.formatted(relicId))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("P001"));

        // 고를 것이 여럿이면 생략할 수 없다.
        post(player, "/api/purifications", "{\"relicId\":%d}".formatted(relicId))
                .andExpect(status().isBadRequest());
    }

    @Test
    @DisplayName("선명할 때 쉬움 한 판은 선명도만 새로 고치고 돌봄 횟수를 하나 올린다")
    void 쉬움은_선명도_회복만() throws Exception {
        Player player = player("산책러");
        int relicId = completedRelic(player);
        clock.advance(Duration.ofDays(6));

        clearPhase(player, relicId, Difficulty.EASY)
                .andExpect(jsonPath("$.data.next.action").value("DONE"))
                .andExpect(jsonPath("$.data.careCount").value(1))
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"));

        // 6일 방치 뒤 갱신했으므로 다시 6일이 지나도 아직 선명하다.
        clock.advance(Duration.ofDays(6));
        get(player, "/api/relics/{id}", relicId)
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.careCount").value(1));
    }

    @Test
    @DisplayName("재봉인이어도 어려움 한 판이면 바로 선명이 된다")
    void 어려움은_한_판에_선명() throws Exception {
        Player player = player("한방이");
        int relicId = completedRelic(player);
        clock.advance(Duration.ofDays(15));

        get(player, "/api/relics/{id}", relicId)
                .andExpect(jsonPath("$.data.clarity").value("RESEALED"))
                .andExpect(jsonPath("$.data.clarityChangesAt").doesNotExist())
                .andExpect(jsonPath("$.data.nextPlay.phase").value("UNSEAL"))
                .andExpect(jsonPath("$.data.nextPlay.remainingPhases.length()").value(2));

        clearPhase(player, relicId, Difficulty.HARD)
                .andExpect(jsonPath("$.data.next.action").value("DONE"))
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.clarityChangesAt").value(startsWith(at(Duration.ofDays(7)))))
                .andExpect(jsonPath("$.data.lastClearedPhase").doesNotExist())
                .andExpect(jsonPath("$.data.careCount").value(1));
    }

    @Test
    @DisplayName("보통은 선명도를 한 칸씩 올린다 — 재봉인에서 선명까지 두 판")
    void 보통은_한_칸씩() throws Exception {
        Player player = player("꾸준이");
        int relicId = completedRelic(player);
        clock.advance(Duration.ofDays(15));

        // 봉인 해제 → 흐려짐. 재봉인까지 흐려짐 구간 7일을 온전히 받는다.
        clearPhase(player, relicId, Difficulty.NORMAL)
                .andExpect(jsonPath("$.data.clarity").value("FADED"))
                .andExpect(jsonPath("$.data.next.action").value("CONTINUE_RESTORE"))
                .andExpect(jsonPath("$.data.next.phase").value("RECALL"))
                .andExpect(jsonPath("$.data.next.remainingPhases.length()").value(1))
                .andExpect(jsonPath("$.data.careCount").value(1));

        get(player, "/api/relics/{id}", relicId)
                .andExpect(jsonPath("$.data.clarity").value("FADED"))
                .andExpect(jsonPath("$.data.lastPurifiedAt").value(startsWith(at(Duration.ZERO))))
                .andExpect(jsonPath("$.data.clarityChangesAt").value(startsWith(at(Duration.ofDays(7)))))
                .andExpect(jsonPath("$.data.nextPlay.phase").value("RECALL"))
                .andExpect(jsonPath("$.data.nextPlay.difficulties[0]").value("NORMAL"));

        // 기억 회복 → 선명. 복구가 끝난다.
        clearPhase(player, relicId, Difficulty.NORMAL)
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.next.action").value("DONE"))
                .andExpect(jsonPath("$.data.careCount").value(2));

        get(player, "/api/relics/{id}", relicId)
                .andExpect(jsonPath("$.data.clarityChangesAt").value(startsWith(at(Duration.ofDays(7)))))
                .andExpect(jsonPath("$.data.nextPlay.phase").value("GUARD"))
                .andExpect(jsonPath("$.data.nextPlay.remainingPhases.length()").value(1));
    }

    @Test
    @DisplayName("흐려짐에서 보통 한 판이면 선명이 된다")
    void 흐려짐은_보통_한_판() throws Exception {
        Player player = player("부지런");
        int relicId = completedRelic(player);
        clock.advance(Duration.ofDays(8));

        get(player, "/api/relics/{id}", relicId)
                .andExpect(jsonPath("$.data.nextPlay.phase").value("RECALL"));

        clearPhase(player, relicId, Difficulty.NORMAL)
                .andExpect(jsonPath("$.data.next.action").value("DONE"))
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.careCount").value(1));
    }

    @Test
    @DisplayName("관리 플레이에 실패하면 선명도와 두 시각, 돌봄 횟수가 그대로다")
    void 실패하면_그대로() throws Exception {
        Player player = player("헛손질");
        int relicId = completedRelic(player);
        UserRelic before = userRelicRepository.findByUserIdAndRelicId(player.id(), relicId).orElseThrow();
        clock.advance(Duration.ofDays(15));

        String token = startPurification(player, relicId, Difficulty.NORMAL);
        clock.advance(Duration.ofMinutes(1));
        post(player, "/api/purifications/{token}/result", """
                {"result":"FAILURE","failReason":"DAMAGE_FULL","tapsUsed":15,"scoutsUsed":3,"damage":100,
                 "bonusTiles":{"jeongseong":0}}
                """, token)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.next.action").value("RETRY"))
                .andExpect(jsonPath("$.data.clarity").value("RESEALED"))
                .andExpect(jsonPath("$.data.careCount").value(0));

        UserRelic after = userRelicRepository.findByUserIdAndRelicId(player.id(), relicId).orElseThrow();
        assertThat(after.getLastPurifiedAt()).isEqualTo(before.getLastPurifiedAt());
        assertThat(after.getClarityAnchorAt()).isEqualTo(before.getClarityAnchorAt());
        assertThat(after.getCareCount()).isZero();
    }

    @Test
    @DisplayName("플레이 도중 흐려짐 경계를 넘어도 그 판은 시작할 때의 계획대로 정산한다")
    void 정산은_세션_시작_시점_계획_기준() throws Exception {
        Player player = player("아슬이");
        int relicId = completedRelic(player);
        // 선명의 마지막 순간에 쉬움을 시작한다.
        clock.advance(Duration.ofDays(7).minusSeconds(30));
        String token = startPurification(player, relicId, Difficulty.EASY);
        clock.advance(Duration.ofMinutes(1));

        post(player, "/api/purifications/{token}/result", """
                {"result":"SUCCESS","tapsUsed":10,"scoutsUsed":1,"damage":0,
                 "bonusTiles":{"jeongseong":0}}
                """, token)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.next.action").value("DONE"))
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"));
    }

    @Test
    @DisplayName("흐려짐에서 기억 회복을 하던 중 재봉인돼도 시작할 때의 흐려짐에서 한 칸 올라 선명이 된다")
    void 플레이_도중_재봉인돼도_계획_선명도에서_한_칸() throws Exception {
        Player player = player("아슬아슬");
        int relicId = completedRelic(player);
        // 흐려짐의 마지막 순간에 기억 회복을 시작하고, 끝낼 때는 재봉인이다.
        clock.advance(Duration.ofDays(14).minusSeconds(30));
        String token = startPurification(player, relicId, Difficulty.NORMAL);
        clock.advance(Duration.ofMinutes(1));

        post(player, "/api/purifications/{token}/result", """
                {"result":"SUCCESS","tapsUsed":10,"scoutsUsed":1,"damage":0,
                 "bonusTiles":{"jeongseong":0}}
                """, token)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.next.action").value("DONE"));
    }

    @Test
    @DisplayName("작업대 정령에도 마지막 정화 시각과 다음에 선명도가 바뀌는 시각이 실린다")
    void 작업대_정령_시각() throws Exception {
        Player player = player("시계공");
        int relicId = completedRelic(player);
        LocalDateTime completedAt = LocalDateTime.now(clock);
        clock.advance(Duration.ofDays(8));

        get(player, "/api/workbench")
                .andExpect(jsonPath("$.data.spirits[0].relicId").value(relicId))
                .andExpect(jsonPath("$.data.spirits[0].clarity").value("FADED"))
                .andExpect(jsonPath("$.data.spirits[0].lastPurifiedAt").value(startsWith(completedAt.toString())))
                .andExpect(jsonPath("$.data.spirits[0].clarityChangesAt")
                        .value(startsWith(completedAt.plusDays(14).toString())));
    }

    @Test
    @DisplayName("성장·각성 API 는 없어졌다")
    void 각성_API_제거() throws Exception {
        Player player = player("옛사람");
        int relicId = completedRelic(player);

        post(player, "/api/relics/{id}/awaken", """
                {"targetStage":"AWAKENED_PLUS"}
                """, relicId)
                // 예전 구현은 게이지 부족으로 409(W003)를 줬다. 경로 자체가 없어졌는지 404 C004 로 확인한다.
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("C004"));

        get(player, "/api/relics/{id}", relicId)
                .andExpect(jsonPath("$.data.awakeningStage").doesNotExist())
                .andExpect(jsonPath("$.data.growthGauge").doesNotExist())
                .andExpect(jsonPath("$.data.growthReady").doesNotExist())
                .andExpect(jsonPath("$.data.nextStage").doesNotExist())
                .andExpect(jsonPath("$.data.careCount").value(0));
    }

    private int completedRelic(Player player) throws Exception {
        int relicId = giveAndReturnId(player, 1);
        completeFirstPurification(player, relicId);
        return relicId;
    }

    @Test
    @DisplayName("오염 관리를 마치기 전에는 이름을 지을 수 없다")
    void 이름_짓기는_완료_후에만() throws Exception {
        Player player = player("작명가");
        int relicId = giveAndReturnId(player, 1);

        patch(player, "/api/relics/{id}/spirit-name", """
                {"name":"향로지기"}
                """, relicId)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("W002"));

        completeFirstPurification(player, relicId);

        patch(player, "/api/relics/{id}/spirit-name", """
                {"name":"향로지기"}
                """, relicId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.relic.spiritName").value("향로지기"));

        // null 은 기본 이름으로 되돌린다.
        patch(player, "/api/relics/{id}/spirit-name", """
                {"name":null}
                """, relicId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.relic.spiritName")
                        .value(catalog.relic(relicId).getSpiritDefaultName()));
    }

    @Test
    @DisplayName("보유하지 않은 유물은 상세도 정화도 W001")
    void 보유하지_않은_유물() throws Exception {
        Player player = player("빈손");

        get(player, "/api/relics/{id}", 1)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("W001"));

        // 존재하지 않는 번호도 같은 응답이다 — 나누면 번호를 찍어 존재 여부를 알아낼 수 있다.
        get(player, "/api/relics/{id}", 999)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("W001"));
    }

    @Test
    @DisplayName("작업대는 돌봐야 할 순서로 정령을 나열한다 — 재봉인 → 흐려짐 → 선명")
    void 작업대_정렬() throws Exception {
        Player player = player("관리자");
        int resealed = giveAndReturnId(player, 1);
        int faded = giveAndReturnId(player, 2);
        int clear = giveAndReturnId(player, 3);

        completeFirstPurification(player, resealed);
        clock.advance(Duration.ofDays(8));
        completeFirstPurification(player, faded);
        clock.advance(Duration.ofDays(8));
        completeFirstPurification(player, clear);

        // 1번은 16일, 2번은 8일 방치됐다.
        get(player, "/api/workbench")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.spirits[0].relicId").value(resealed))
                .andExpect(jsonPath("$.data.spirits[0].clarity").value("RESEALED"))
                .andExpect(jsonPath("$.data.spirits[1].relicId").value(faded))
                .andExpect(jsonPath("$.data.spirits[1].clarity").value("FADED"))
                .andExpect(jsonPath("$.data.spirits[2].relicId").value(clear))
                .andExpect(jsonPath("$.data.spirits[2].clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.lockedCount").value(6))
                .andExpect(jsonPath("$.data.capacity").value(3));
    }

    /** 지금 시계에서 {@code offset} 만큼 뒤의 시각. 응답 시각과 앞부분을 비교한다. */
    private String at(Duration offset) {
        return LocalDateTime.now(clock).plus(offset).toString();
    }

    /** 유물을 쥐여 주고 그 번호를 돌려준다. */
    private int giveAndReturnId(Player player, int relicId) {
        give(player, relicId);
        return relicId;
    }
}
