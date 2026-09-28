package com.example.jamkkaebi.coreloop;

import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.UserBuilding;
import com.example.jamkkaebi.artifact.service.EraCrystalWallet;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;

import java.time.LocalDateTime;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 작업대의 건물 — 상세 조회와 시대의 결정으로 강화.
 *
 * <p>비용은 코드 기본값(Lv.2 30 · Lv.3 60)을 쓴다. 테스트 설정에는 재화 값이 없다.
 */
class BuildingLevelUpTest extends CoreLoopApiTestSupport {

    @Autowired
    private EraCrystalWallet wallet;

    @Test
    @DisplayName("건물 상세는 레벨·자막·잔액과 다음 레벨 비용을 준다")
    void 건물_상세() throws Exception {
        Player player = player("건축가");
        int buildingId = giveBuilding(player, Era.GORYEO);
        grantCrystal(player, Era.GORYEO, 12);

        get(player, "/api/buildings/{id}", buildingId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("OK"))
                .andExpect(jsonPath("$.data.buildingId").value(buildingId))
                .andExpect(jsonPath("$.data.era").value("GORYEO"))
                .andExpect(jsonPath("$.data.level").value(1))
                .andExpect(jsonPath("$.data.maxLevel").value(3))
                .andExpect(jsonPath("$.data.story").isNotEmpty())
                .andExpect(jsonPath("$.data.displayed").value(false))
                .andExpect(jsonPath("$.data.balance").value(12))
                .andExpect(jsonPath("$.data.nextLevel.level").value(2))
                .andExpect(jsonPath("$.data.nextLevel.cost").value(30))
                .andExpect(jsonPath("$.data.nextLevel.affordable").value(false));
    }

    @Test
    @DisplayName("그 시대의 결정을 써서 Lv.3 까지 올리고, 최대 레벨이면 다음 레벨 정보가 빠진다")
    void 건물_강화_최대_레벨까지() throws Exception {
        Player player = player("건축가");
        int buildingId = giveBuilding(player, Era.GORYEO);
        grantCrystal(player, Era.GORYEO, 100);
        // 다른 시대의 결정은 건드리지 않는다.
        grantCrystal(player, Era.JOSEON, 50);

        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":2}", buildingId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("BUILDING_LEVELED_UP"))
                .andExpect(jsonPath("$.data.level").value(2))
                .andExpect(jsonPath("$.data.spent").value(30))
                .andExpect(jsonPath("$.data.balance").value(70))
                .andExpect(jsonPath("$.data.nextLevel.level").value(3))
                .andExpect(jsonPath("$.data.nextLevel.cost").value(60))
                .andExpect(jsonPath("$.data.nextLevel.affordable").value(true));

        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":3}", buildingId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.level").value(3))
                .andExpect(jsonPath("$.data.spent").value(60))
                .andExpect(jsonPath("$.data.balance").value(10))
                .andExpect(jsonPath("$.data.nextLevel").doesNotExist());

        assertThat(crystalOf(player, Era.GORYEO)).isEqualTo(10);
        assertThat(crystalOf(player, Era.JOSEON)).isEqualTo(50);

        // 작업대 목록의 건물 레벨도 따라온다.
        get(player, "/api/workbench")
                .andExpect(jsonPath("$.data.buildings[0].level").value(3));
    }

    @Test
    @DisplayName("같은 목표 레벨로 다시 누르면 결정을 빼지 않고 200 을 준다")
    void 건물_강화_멱등() throws Exception {
        Player player = player("건축가");
        int buildingId = giveBuilding(player, Era.GORYEO);
        grantCrystal(player, Era.GORYEO, 40);

        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":2}", buildingId)
                .andExpect(status().isOk());
        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":2}", buildingId)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.level").value(2))
                .andExpect(jsonPath("$.data.spent").value(0))
                .andExpect(jsonPath("$.data.balance").value(10));

        assertThat(crystalOf(player, Era.GORYEO)).isEqualTo(10);
    }

    @Test
    @DisplayName("결정이 모자라면 BD002 이고 아무것도 빠지지 않는다")
    void 결정_부족() throws Exception {
        Player player = player("건축가");
        int buildingId = giveBuilding(player, Era.GORYEO);
        grantCrystal(player, Era.GORYEO, 29);

        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":2}", buildingId)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("BD002"));

        assertThat(crystalOf(player, Era.GORYEO)).isEqualTo(29);
        assertThat(levelOf(player, buildingId)).isEqualTo(1);
    }

    @Test
    @DisplayName("지갑이 아예 없어도 BD002 다")
    void 지갑_없음() throws Exception {
        Player player = player("건축가");
        int buildingId = giveBuilding(player, Era.GORYEO);

        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":2}", buildingId)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("BD002"));
    }

    @Test
    @DisplayName("현재 +1 도 현재도 아닌 목표 레벨은 BD003 이다")
    void 레벨_불일치() throws Exception {
        Player player = player("건축가");
        int buildingId = giveBuilding(player, Era.GORYEO);
        grantCrystal(player, Era.GORYEO, 100);

        // Lv.1 에서 바로 Lv.3 — 연타로 두 단계가 오르는 사고를 막는다.
        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":3}", buildingId)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("BD003"));

        assertThat(crystalOf(player, Era.GORYEO)).isEqualTo(100);
    }

    @Test
    @DisplayName("목표 레벨이 범위를 벗어나거나 없으면 C001 이다")
    void 입력_검증() throws Exception {
        Player player = player("건축가");
        int buildingId = giveBuilding(player, Era.GORYEO);

        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":1}", buildingId)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":4}", buildingId)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
        post(player, "/api/buildings/{id}/level-up", "{}", buildingId)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
    }

    @Test
    @DisplayName("받지 않은 건물과 없는 번호는 같은 BD001 이다")
    void 미보유_건물() throws Exception {
        Player player = player("건축가");
        Player other = player("이웃");
        int buildingId = giveBuilding(other, Era.GORYEO);

        get(player, "/api/buildings/{id}", buildingId)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("BD001"));
        get(player, "/api/buildings/{id}", 999)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("BD001"));
        post(player, "/api/buildings/{id}/level-up", "{\"targetLevel\":2}", buildingId)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("BD001"));
    }

    /** 시대 유물 3종을 돌리지 않고 건물을 쥐여 준다. 지급 자체는 {@code CoreLoopFlowTest} 가 본다. */
    private int giveBuilding(Player player, Era era) {
        int buildingId = catalog.buildingOf(era).orElseThrow().getId();
        userBuildingRepository.save(UserBuilding.awarded(player.id(), buildingId, LocalDateTime.now(clock)));
        return buildingId;
    }

    // 적립은 UPDATE 한 문장이라 트랜잭션 안에서만 돈다.
    private void grantCrystal(Player player, Era era, int amount) {
        transactionTemplate.executeWithoutResult(status -> wallet.add(player.id(), era, amount));
    }

    private int levelOf(Player player, int buildingId) {
        return userBuildingRepository.findByUserIdAndBuildingId(player.id(), buildingId)
                .orElseThrow().getLevel();
    }
}
