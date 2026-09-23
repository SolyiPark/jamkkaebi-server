package com.example.jamkkaebi.coreloop;

import com.example.jamkkaebi.artifact.domain.ArtifactStatus;
import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Difficulty;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 코어 루프 완주 — 상자에서 뽑아 3페이즈를 마치고 각성까지.
 *
 * <p>백엔드 계획 M1 의 완료 판정이 그대로 이 파일의 첫 테스트다.
 */
class CoreLoopFlowTest extends CoreLoopApiTestSupport {

    @Test
    @DisplayName("상자를 열어 얻은 유물을 3페이즈까지 마치면 완료 상태가 되고 이름이 공개된다")
    void 상자에서_뽑아_정령_수호까지() throws Exception {
        Player player = player("잠꾸러기");

        var opened = post(player, "/api/box/open", """
                {"source":"FREE","requestId":"3f1c9a52-6a1e-4c1b-9d0e-2b7f0c8a4e11"}
                """)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("BOX_OPENED"))
                .andExpect(jsonPath("$.data.result").value("NEW"))
                // 획득 직후는 실루엣뿐이다 — 시적 별칭은 봉인 해제를 마쳐야 공개된다 (부록 C-1).
                .andExpect(jsonPath("$.data.artifact.revealLevel").value("SILHOUETTE"))
                .andExpect(jsonPath("$.data.artifact.poeticAlias").doesNotExist())
                .andExpect(jsonPath("$.data.artifact.realName").doesNotExist());
        int artifactId = read(opened, "$.data.artifact.artifactId");

        // 1페이즈 — 봉인 해제. 별칭이 열린다.
        clearPhase(player, artifactId, null)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.revealed").value("ALIAS"))
                .andExpect(jsonPath("$.data.next.action").value("NEXT_PHASE"))
                .andExpect(jsonPath("$.data.next.phase").value("RECALL"))
                // 최초 정화는 성장 게이지를 주지 않는다 — 완료된 도깨비를 돌본 성과로만 자란다.
                .andExpect(jsonPath("$.data.rewards.growthGauge").value(0));

        // 2페이즈 — 기억 회복. 형태가 열린다.
        clearPhase(player, artifactId, null)
                .andExpect(jsonPath("$.data.revealed").value("FORM"))
                .andExpect(jsonPath("$.data.next.phase").value("GUARD"));

        // 3페이즈 — 정령 수호. 이름이 전부 공개되고 각성한다.
        clearPhase(player, artifactId, null)
                .andExpect(jsonPath("$.data.status").value("COMPLETED"))
                .andExpect(jsonPath("$.data.revealed").value("NAMED"))
                .andExpect(jsonPath("$.data.artifact.realName").isNotEmpty())
                .andExpect(jsonPath("$.data.artifact.spiritName").isNotEmpty())
                .andExpect(jsonPath("$.data.next.action").value("DONE"))
                .andExpect(jsonPath("$.data.awakening.stage").value("AWAKENED"))
                .andExpect(jsonPath("$.data.clarity").value("CLEAR"));

        UserArtifact stored = userArtifactRepository
                .findByUserIdAndArtifactId(player.id(), artifactId).orElseThrow();
        assertThat(stored.getStatus()).isEqualTo(ArtifactStatus.COMPLETED);
        assertThat(stored.getAwakeningStage()).isEqualTo(AwakeningStage.AWAKENED);
        assertThat(stored.getLastPurifiedAt()).isNotNull();
    }

    @Test
    @DisplayName("한 시대의 유물 3종을 모두 마치면 건물이 자동으로 지급된다")
    void 시대_완성_건물_지급() throws Exception {
        Player player = player("수집가");
        List<Integer> goryeo = artifactIdsOf(Era.GORYEO);

        completeFirstPurification(player, giveAndReturnId(player, goryeo.get(0)));
        completeFirstPurification(player, giveAndReturnId(player, goryeo.get(1)));
        assertThat(userBuildingRepository.findAllByUserId(player.id())).isEmpty();

        completeFirstPurification(player, giveAndReturnId(player, goryeo.get(2)))
                .andExpect(jsonPath("$.data.buildingAwarded.era").value("GORYEO"))
                .andExpect(jsonPath("$.data.buildingAwarded.level").value(1));

        assertThat(userBuildingRepository.findAllByUserId(player.id())).hasSize(1);
    }

    @Test
    @DisplayName("관리 플레이로 게이지를 채우면 강화를 확정할 수 있고, 넘친 게이지는 다음 단계로 이월된다")
    void 성장_게이지_충전과_강화_확정() throws Exception {
        Player player = player("돌보미");
        int artifactId = giveAndReturnId(player, 1);
        completeFirstPurification(player, artifactId);

        // 보통 난이도 한 판 = 20 × 1.2 = 24. 각성+ 에 필요한 100 을 넘기려면 다섯 판.
        for (int i = 0; i < 5; i++) {
            clearPhase(player, artifactId, Difficulty.NORMAL)
                    .andExpect(jsonPath("$.data.rewards.growthGauge").value(24));
        }

        get(player, "/api/artifacts/{id}", artifactId)
                .andExpect(jsonPath("$.data.growthReady").value(true))
                .andExpect(jsonPath("$.data.growthGauge.current").value(120))
                .andExpect(jsonPath("$.data.growthGauge.max").value(100))
                .andExpect(jsonPath("$.data.nextStage").value("AWAKENED_PLUS"))
                // 어려움은 각성+ 전까지 잠겨 있다.
                .andExpect(jsonPath("$.data.nextPlay.difficulties[3].difficulty").value("HARD"))
                .andExpect(jsonPath("$.data.nextPlay.difficulties[3].unlocked").value(false));

        post(player, "/api/artifacts/{id}/awaken", """
                {"targetStage":"AWAKENED_PLUS"}
                """, artifactId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.awakeningStage").value("AWAKENED_PLUS"))
                // 넘친 20 은 버리지 않고 다음 단계로 옮긴다 (부록 C-7).
                .andExpect(jsonPath("$.data.carriedOver").value(20))
                .andExpect(jsonPath("$.data.growthGauge.current").value(20))
                .andExpect(jsonPath("$.data.growthGauge.max").value(150))
                .andExpect(jsonPath("$.data.unlockedDifficulties[0]").value("HARD"));
    }

    @Test
    @DisplayName("같은 단계로 다시 확정 요청하면 아무것도 바뀌지 않고, 두 단계를 건너뛰면 막힌다")
    void 강화_확정_멱등과_단계_검사() throws Exception {
        Player player = player("연타왕");
        int artifactId = giveAndReturnId(player, 1);
        completeFirstPurification(player, artifactId);
        for (int i = 0; i < 5; i++) {
            clearPhase(player, artifactId, Difficulty.NORMAL);
        }
        post(player, "/api/artifacts/{id}/awaken", """
                {"targetStage":"AWAKENED_PLUS"}
                """, artifactId).andExpect(status().isOk());

        // 응답을 못 받고 다시 누른 경우 — 단계가 또 오르면 안 된다.
        post(player, "/api/artifacts/{id}/awaken", """
                {"targetStage":"AWAKENED_PLUS"}
                """, artifactId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.awakeningStage").value("AWAKENED_PLUS"))
                .andExpect(jsonPath("$.data.carriedOver").value(0));

        assertThat(userArtifactRepository.findByUserIdAndArtifactId(player.id(), artifactId)
                .orElseThrow().getAwakeningStage()).isEqualTo(AwakeningStage.AWAKENED_PLUS);
    }

    @Test
    @DisplayName("게이지가 차지 않았는데 강화하면 W003, 각성 단계가 어긋나면 W004")
    void 강화_실패_경로() throws Exception {
        Player player = player("성급이");
        int artifactId = giveAndReturnId(player, 1);
        completeFirstPurification(player, artifactId);

        post(player, "/api/artifacts/{id}/awaken", """
                {"targetStage":"AWAKENED_PLUS"}
                """, artifactId)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("W003"));

        post(player, "/api/artifacts/{id}/awaken", """
                {"targetStage":"FULLY_AWAKENED"}
                """, artifactId)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("W004"));
    }

    @Test
    @DisplayName("정령 수호를 마치기 전에는 이름을 지을 수 없다")
    void 이름_짓기는_완료_후에만() throws Exception {
        Player player = player("작명가");
        int artifactId = giveAndReturnId(player, 1);

        patch(player, "/api/artifacts/{id}/spirit-name", """
                {"name":"향로지기"}
                """, artifactId)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("W002"));

        completeFirstPurification(player, artifactId);

        patch(player, "/api/artifacts/{id}/spirit-name", """
                {"name":"향로지기"}
                """, artifactId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.artifact.spiritName").value("향로지기"));

        // null 은 기본 이름으로 되돌린다.
        patch(player, "/api/artifacts/{id}/spirit-name", """
                {"name":null}
                """, artifactId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.artifact.spiritName")
                        .value(catalog.artifact(artifactId).getSpiritDefaultName()));
    }

    @Test
    @DisplayName("보유하지 않은 유물은 상세도 정화도 W001")
    void 보유하지_않은_유물() throws Exception {
        Player player = player("빈손");

        get(player, "/api/artifacts/{id}", 1)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("W001"));

        // 존재하지 않는 번호도 같은 응답이다 — 나누면 번호를 찍어 존재 여부를 알아낼 수 있다.
        get(player, "/api/artifacts/{id}", 999)
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
                .andExpect(jsonPath("$.data.spirits[0].artifactId").value(resealed))
                .andExpect(jsonPath("$.data.spirits[0].clarity").value("RESEALED"))
                .andExpect(jsonPath("$.data.spirits[1].artifactId").value(faded))
                .andExpect(jsonPath("$.data.spirits[1].clarity").value("FADED"))
                .andExpect(jsonPath("$.data.spirits[2].artifactId").value(clear))
                .andExpect(jsonPath("$.data.spirits[2].clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.lockedCount").value(6))
                .andExpect(jsonPath("$.data.capacity").value(3));
    }

    /** 유물을 쥐여 주고 그 번호를 돌려준다. */
    private int giveAndReturnId(Player player, int artifactId) {
        give(player, artifactId);
        return artifactId;
    }
}
