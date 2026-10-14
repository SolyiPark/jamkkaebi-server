package com.example.jamkkaebi.coreloop;

import com.example.jamkkaebi.friend.domain.Friendship;
import com.example.jamkkaebi.friend.repository.FriendRecommendationRepository;
import com.example.jamkkaebi.friend.repository.FriendRequestRepository;
import com.example.jamkkaebi.friend.repository.FriendshipRepository;
import com.example.jamkkaebi.friend.repository.VisitLogRepository;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.UserBuilding;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.test.web.servlet.ResultActions;

import java.time.LocalDateTime;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.hamcrest.Matchers.nullValue;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 소셜 — 다른 플레이어의 실제 정보로 만든 오늘의 추천과 친구 카드, 친구 전시관 구경.
 *
 * <p>친구 기능 테스트({@code friend} 패키지)는 전시관을 대역으로 바꿔 끼우지만, 여기서는 <b>아무것도 바꿔
 * 끼우지 않는다</b>. 정화로 깨운 도깨비, 전시관 배치, 진행도 집계가 실제 구현을 거쳐 친구 화면까지 오는지를 본다.
 */
class SocialFlowTest extends CoreLoopApiTestSupport {

    private static final int INCENSE_BURNER = 2;  // 금동대향로 — 삼국
    private static final int GEUMGWAN = 3;        // 금관총 금관 — 삼국
    private static final int MAITREYA = 4;        // 관촉사 석조미륵보살입상 — 고려
    private static final int CELADON = 5;         // 상감청자 — 고려
    private static final int ROYAL_SEAL = 7;      // 유서지보 — 조선
    private static final int MAPAE = 8;           // 마패 — 조선

    @Autowired
    private FriendshipRepository friendshipRepository;
    @Autowired
    private FriendRequestRepository friendRequestRepository;
    @Autowired
    private FriendRecommendationRepository recommendationRepository;
    @Autowired
    private VisitLogRepository visitLogRepository;

    @AfterEach
    void clearSocialTables() {
        friendshipRepository.deleteAll();
        friendRequestRepository.deleteAll();
        recommendationRepository.deleteAll();
        visitLogRepository.deleteAll();
    }

    @Test
    @DisplayName("정령 수호까지 마친 도깨비만 아바타로 고를 수 있고, 고른 아바타는 친구 카드에 그대로 나간다")
    void 아바타() throws Exception {
        Player me = player("향로지기");
        Player friend = player("이끼돌");
        friendshipRepository.save(Friendship.between(me.id(), friend.id()));
        giveCompleted(me, INCENSE_BURNER);
        give(me, CELADON); // 작업대에서 진행 중

        patch(me, "/api/users/me/avatar", "{\"spiritId\":%d}".formatted(INCENSE_BURNER))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("AVATAR_UPDATED"))
                .andExpect(jsonPath("$.data.spiritId").value(INCENSE_BURNER));
        // 같은 값으로 다시 보내도 성공이다.
        patch(me, "/api/users/me/avatar", "{\"spiritId\":%d}".formatted(INCENSE_BURNER))
                .andExpect(status().isOk());

        patch(me, "/api/users/me/avatar", "{\"spiritId\":%d}".formatted(CELADON))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("U002"));
        patch(me, "/api/users/me/avatar", "{\"spiritId\":%d}".formatted(GEUMGWAN))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("U002"));
        patch(me, "/api/users/me/avatar", "{\"spiritId\":99}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"))
                .andExpect(jsonPath("$.errors[0].field").value("spiritId"));

        // 실패한 요청은 아무것도 바꾸지 않았다.
        get(me, "/api/users/me").andExpect(jsonPath("$.data.spiritId").value(INCENSE_BURNER));
        get(friend, "/api/friends")
                .andExpect(jsonPath("$.data.friends[0].friendCode").value(me.code()))
                .andExpect(jsonPath("$.data.friends[0].spiritId").value(INCENSE_BURNER));

        patch(me, "/api/users/me/avatar", "{\"spiritId\":null}")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.spiritId").value(nullValue()));
        // 기본 아바타면 친구 카드에서는 키가 빠진다.
        get(friend, "/api/friends").andExpect(jsonPath("$.data.friends[0].spiritId").doesNotExist());
    }

    @Test
    @DisplayName("오늘의 추천에는 다른 플레이어의 실제 수집률·시대별 복원 수·아바타가 실린다")
    void 오늘의_추천_카드() throws Exception {
        Player me = player("나");
        Player collector = player("수집가");
        Player beginner = player("새내기");
        Player newcomer = player("빈손");
        giveCompleted(collector, 1);
        giveCompleted(collector, INCENSE_BURNER);
        giveCompleted(collector, MAITREYA);
        give(collector, MAPAE); // 진행 중인 유물은 수집률에 들어가지 않는다
        giveCompleted(beginner, ROYAL_SEAL);
        patch(collector, "/api/users/me/avatar", "{\"spiritId\":%d}".formatted(INCENSE_BURNER))
                .andExpect(status().isOk());

        ResultActions picks = get(me, "/api/friends/recommendations")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.players.length()").value(3))
                .andExpect(jsonPath("$.data.remaining").value(3));

        Map<String, Object> collectorCard = card(picks, collector);
        assertThat(collectorCard)
                .containsEntry("nickname", "수집가")
                .containsEntry("spiritId", INCENSE_BURNER)
                .containsEntry("collectionRate", 33);
        assertThat(restoredByEra(collectorCard)).containsExactly(
                Map.of("era", "THREE_KINGDOMS", "completedCount", 2),
                Map.of("era", "GORYEO", "completedCount", 1));

        Map<String, Object> beginnerCard = card(picks, beginner);
        assertThat(beginnerCard).containsEntry("collectionRate", 11).doesNotContainKey("spiritId");
        assertThat(restoredByEra(beginnerCard)).containsExactly(Map.of("era", "JOSEON", "completedCount", 1));

        assertThat(card(picks, newcomer)).containsEntry("collectionRate", 0);
        assertThat(restoredByEra(card(picks, newcomer))).isEmpty();
    }

    @Test
    @DisplayName("친구가 되면 그 사람이 직접 깨운 도깨비와 받은 건물을 전시관에서 구경할 수 있다")
    void 친구_전시관_구경() throws Exception {
        Player owner = player("주인");
        Player visitor = player("방문객");
        Player stranger = player("낯선이");

        // 주인은 실제로 세 페이즈를 정화해 향로의 도깨비를 깨우고 이름을 지어 준다.
        give(owner, INCENSE_BURNER);
        completeFirstPurification(owner, INCENSE_BURNER).andExpect(jsonPath("$.data.status").value("COMPLETED"));
        patch(owner, "/api/relics/{id}/spirit-name", "{\"name\":\"향로지기\"}", INCENSE_BURNER)
                .andExpect(status().isOk());
        giveCompleted(owner, MAPAE);
        int buildingId = catalog.buildingOf(Era.GORYEO).orElseThrow().getId();
        userBuildingRepository.save(UserBuilding.awarded(owner.id(), buildingId, LocalDateTime.now(clock)));
        put(owner, "/api/exhibition/layout", """
                {"items":[{"type":"SPIRIT","id":%d,"x":3,"y":5},{"type":"SPIRIT","id":%d,"x":1,"y":2},
                          {"type":"BUILDING","id":%d,"x":5,"y":4}]}
                """.formatted(INCENSE_BURNER, MAPAE, buildingId))
                .andExpect(status().isOk());
        patch(owner, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":true}", MAPAE)
                .andExpect(status().isOk());
        patch(owner, "/api/users/me/avatar", "{\"spiritId\":%d}".formatted(INCENSE_BURNER))
                .andExpect(status().isOk());

        // 친구 코드로 요청하고 수락해 친구가 된다.
        post(visitor, "/api/friend-requests", "{\"friendCode\":\"%s\"}".formatted(owner.code()))
                .andExpect(status().isCreated());
        post(owner, "/api/friend-requests/{code}/accept", "", visitor.code())
                .andExpect(jsonPath("$.code").value("FRIEND_ADDED"));

        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.owner.friendCode").value(owner.code()))
                .andExpect(jsonPath("$.data.owner.spiritId").value(INCENSE_BURNER))
                .andExpect(jsonPath("$.data.owner.collectionRate").value(22))
                // 숨긴 도깨비(마패)는 빠지고, 깨운 도깨비 하나와 건물 하나가 보인다.
                .andExpect(jsonPath("$.data.spirits.length()").value(1))
                .andExpect(jsonPath("$.data.spirits[0].relicId").value(INCENSE_BURNER))
                .andExpect(jsonPath("$.data.spirits[0].era").value("THREE_KINGDOMS"))
                .andExpect(jsonPath("$.data.spirits[0].realName").value("금동대향로"))
                .andExpect(jsonPath("$.data.spirits[0].poeticAlias").value("백제, 향이 스민 산맥"))
                .andExpect(jsonPath("$.data.spirits[0].spiritName").value("향로지기"))
                .andExpect(jsonPath("$.data.spirits[0].awakeningStage").value("AWAKENED"))
                .andExpect(jsonPath("$.data.spirits[0].position.x").value(3))
                .andExpect(jsonPath("$.data.spirits[0].clarity").doesNotExist())
                .andExpect(jsonPath("$.data.buildings.length()").value(1))
                .andExpect(jsonPath("$.data.buildings[0].buildingId").value(buildingId))
                .andExpect(jsonPath("$.data.buildings[0].name").value("경천사지 십층석탑"))
                .andExpect(jsonPath("$.data.buildings[0].level").value(1))
                .andExpect(jsonPath("$.data.buildings[0].position.x").value(5))
                .andExpect(jsonPath("$.data.visitRewardAvailable").value(true));

        post(visitor, "/api/friends/{code}/visit-reward", "", owner.code())
                .andExpect(jsonPath("$.data.jeongseongGained").value(5));
        get(stranger, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("F008"));
    }

    @Test
    @DisplayName("아직 도깨비도 건물도 놓지 않은 친구의 전시관은 오류가 아니라 그리드만 있는 기본 빈 전시관이다")
    void 빈_전시관_방문() throws Exception {
        Player owner = player("새내기");
        Player visitor = player("방문객");
        friendshipRepository.save(Friendship.between(owner.id(), visitor.id()));

        // 한 번도 접속하지 않아 스냅샷조차 없는 친구
        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.owner.friendCode").value(owner.code()))
                .andExpect(jsonPath("$.data.grid.width").value(7))
                .andExpect(jsonPath("$.data.grid.height").value(7))
                .andExpect(jsonPath("$.data.spirits").isEmpty())
                .andExpect(jsonPath("$.data.buildings").isEmpty())
                .andExpect(jsonPath("$.data.capturedAt").doesNotExist())
                .andExpect(jsonPath("$.data.visitRewardAvailable").value(true));

        // 접속은 했지만 깨운 도깨비를 아직 전시관에 놓지 않은 친구 — 접속 때 찍힌 빈 스냅샷이 보인다.
        giveCompleted(owner, INCENSE_BURNER);
        get(owner, "/api/users/me").andExpect(status().isOk());

        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.grid.width").value(7))
                .andExpect(jsonPath("$.data.spirits").isEmpty())
                .andExpect(jsonPath("$.data.buildings").isEmpty())
                .andExpect(jsonPath("$.data.capturedAt").exists());
        // 빈 전시관이어도 방문 보상은 그대로 받는다.
        post(visitor, "/api/friends/{code}/visit-reward", "", owner.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.jeongseongGained").value(5));
    }

    private Map<String, Object> card(ResultActions picks, Player player) throws Exception {
        List<Map<String, Object>> found = read(picks,
                "$.data.players[?(@.friendCode == '%s')]".formatted(player.code()));
        assertThat(found).as("%s 가 오늘의 추천에 있어야 한다", player.code()).hasSize(1);
        return found.getFirst();
    }

    @SuppressWarnings("unchecked")
    private static List<Map<String, Object>> restoredByEra(Map<String, Object> card) {
        return (List<Map<String, Object>>) card.get("restoredByEra");
    }
}
