package com.example.jamkkaebi.coreloop;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.domain.UserBuilding;
import com.example.jamkkaebi.artifact.service.EraCrystalWallet;
import com.example.jamkkaebi.friend.domain.Friendship;
import com.example.jamkkaebi.exhibition.service.ExhibitionViewProvider;
import com.example.jamkkaebi.friend.repository.FriendshipRepository;
import com.example.jamkkaebi.friend.service.ExhibitionSnapshotService;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.user.domain.UserActivityRecordedEvent;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.request.MockMvcRequestBuilders;

import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.TransactionDefinition;
import org.springframework.transaction.support.TransactionTemplate;

import java.time.Duration;
import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 내 전시관 — 조회, 배치 저장, 도깨비 숨기기, 그리고 친구에게 보이는 모습.
 *
 * <p>도깨비 번호는 시대 순서대로 1~3 삼국, 4~6 고려, 7~9 조선이다.
 */
class ExhibitionTest extends CoreLoopApiTestSupport {

    private static final int INCENSE_BURNER = 2;  // 금동대향로 — 삼국
    private static final int GEUMGWAN = 3;        // 금관총 금관 — 삼국
    private static final int MAPAE = 8;           // 마패 — 조선

    @Autowired
    private FriendshipRepository friendshipRepository;
    @Autowired
    private ExhibitionSnapshotService snapshotService;
    @Autowired
    private ExhibitionViewProvider viewProvider;
    @Autowired
    private PlatformTransactionManager transactionManager;
    @Autowired
    private EraCrystalWallet wallet;

    @AfterEach
    void clearFriendships() {
        friendshipRepository.deleteAll();
    }

    @Test
    @DisplayName("아무것도 없는 사용자의 전시관은 그리드만 있고 비어 있다")
    void 빈_전시관() throws Exception {
        Player player = player("신입");

        get(player, "/api/exhibition")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("OK"))
                .andExpect(jsonPath("$.data.grid.width").value(20))
                .andExpect(jsonPath("$.data.grid.height").value(20))
                .andExpect(jsonPath("$.data.spirits").isEmpty())
                .andExpect(jsonPath("$.data.buildings").isEmpty());
    }

    @Test
    @DisplayName("정령 수호까지 마친 도깨비와 받은 건물만 담기고, 배치 전에는 좌표 키가 없다")
    void 조회() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        giveCompleted(player, MAPAE);
        give(player, GEUMGWAN); // 작업 중 — 전시관 대상이 아니다
        int buildingId = giveBuilding(player, Era.GORYEO);

        get(player, "/api/exhibition")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.spirits.length()").value(2))
                .andExpect(jsonPath("$.data.spirits[0].artifactId").value(INCENSE_BURNER))
                .andExpect(jsonPath("$.data.spirits[0].era").value("THREE_KINGDOMS"))
                .andExpect(jsonPath("$.data.spirits[0].revealLevel").value("NAMED"))
                .andExpect(jsonPath("$.data.spirits[0].poeticAlias").value("백제, 향이 스민 산맥"))
                .andExpect(jsonPath("$.data.spirits[0].realName").value("금동대향로"))
                .andExpect(jsonPath("$.data.spirits[0].spiritName").value("향산이"))
                .andExpect(jsonPath("$.data.spirits[0].awakeningStage").value("AWAKENED"))
                .andExpect(jsonPath("$.data.spirits[0].clarity").value("CLEAR"))
                .andExpect(jsonPath("$.data.spirits[0].displayed").value(false))
                .andExpect(jsonPath("$.data.spirits[0].hidden").value(false))
                .andExpect(jsonPath("$.data.spirits[0].position").doesNotExist())
                .andExpect(jsonPath("$.data.spirits[1].artifactId").value(MAPAE))
                .andExpect(jsonPath("$.data.buildings.length()").value(1))
                .andExpect(jsonPath("$.data.buildings[0].buildingId").value(buildingId))
                .andExpect(jsonPath("$.data.buildings[0].era").value("GORYEO"))
                .andExpect(jsonPath("$.data.buildings[0].level").value(1))
                .andExpect(jsonPath("$.data.buildings[0].displayed").value(false))
                .andExpect(jsonPath("$.data.buildings[0].position").doesNotExist());
    }

    @Test
    @DisplayName("지어 준 이름과 각성 단계, 방치로 달라진 선명도가 그대로 실린다")
    void 이름_각성_선명도() throws Exception {
        Player player = player("학예사");
        UserArtifact artifact = giveCompleted(player, INCENSE_BURNER);
        patch(player, "/api/artifacts/{id}/spirit-name", "{\"name\":\"향로지기\"}", INCENSE_BURNER)
                .andExpect(status().isOk());
        setAwakening(artifact, AwakeningStage.AWAKENED_PLUS);

        clock.advance(Duration.ofDays(8));
        get(player, "/api/exhibition")
                .andExpect(jsonPath("$.data.spirits[0].spiritName").value("향로지기"))
                .andExpect(jsonPath("$.data.spirits[0].awakeningStage").value("AWAKENED_PLUS"))
                .andExpect(jsonPath("$.data.spirits[0].clarity").value("FADED"));

        clock.advance(Duration.ofDays(7));
        get(player, "/api/exhibition")
                .andExpect(jsonPath("$.data.spirits[0].clarity").value("RESEALED"))
                // 재봉인돼도 공개 단계는 내려가지 않는다 — 실루엣 연출은 클라이언트가 clarity 로 한다.
                .andExpect(jsonPath("$.data.spirits[0].revealLevel").value("NAMED"));
    }

    @Test
    @DisplayName("배치를 저장하면 좌표가 실리고, 다시 조회해도 같다")
    void 배치_저장() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        int buildingId = giveBuilding(player, Era.GORYEO);

        put(player, "/api/exhibition/layout", layout(
                item("SPIRIT", INCENSE_BURNER, 6, 11),
                item("BUILDING", buildingId, 10, 4)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("EXHIBITION_SAVED"))
                .andExpect(jsonPath("$.data.spirits[0].displayed").value(true))
                .andExpect(jsonPath("$.data.spirits[0].position.x").value(6))
                .andExpect(jsonPath("$.data.spirits[0].position.y").value(11))
                .andExpect(jsonPath("$.data.buildings[0].displayed").value(true))
                .andExpect(jsonPath("$.data.buildings[0].position.x").value(10))
                .andExpect(jsonPath("$.data.buildings[0].position.y").value(4));

        get(player, "/api/exhibition")
                .andExpect(jsonPath("$.data.spirits[0].position.x").value(6))
                .andExpect(jsonPath("$.data.spirits[0].position.y").value(11))
                .andExpect(jsonPath("$.data.buildings[0].position.x").value(10))
                .andExpect(jsonPath("$.data.buildings[0].position.y").value(4));

        // 작업대의 상세도 배치 여부를 같이 보여 준다.
        get(player, "/api/artifacts/{id}", INCENSE_BURNER)
                .andExpect(jsonPath("$.data.displayed").value(true));
        get(player, "/api/buildings/{id}", buildingId)
                .andExpect(jsonPath("$.data.displayed").value(true));
    }

    @Test
    @DisplayName("그리드의 끝 칸까지 둘 수 있다")
    void 그리드_경계() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        giveCompleted(player, MAPAE);

        put(player, "/api/exhibition/layout", layout(
                item("SPIRIT", INCENSE_BURNER, 0, 0),
                item("SPIRIT", MAPAE, 19, 19)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.spirits[1].position.x").value(19))
                .andExpect(jsonPath("$.data.spirits[1].position.y").value(19));
    }

    @Test
    @DisplayName("같은 본문을 다시 보내도 결과가 같다")
    void 배치_저장_멱등() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        String body = layout(item("SPIRIT", INCENSE_BURNER, 6, 11));

        put(player, "/api/exhibition/layout", body).andExpect(status().isOk());
        put(player, "/api/exhibition/layout", body)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.spirits.length()").value(1))
                .andExpect(jsonPath("$.data.spirits[0].position.x").value(6))
                .andExpect(jsonPath("$.data.spirits[0].position.y").value(11));
    }

    @Test
    @DisplayName("목록에 없는 항목은 전시관에서 내려가고, 위치만 바꾸면 옮겨진다")
    void 전체_교체() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        giveCompleted(player, MAPAE);
        int buildingId = giveBuilding(player, Era.JOSEON);

        put(player, "/api/exhibition/layout", layout(
                item("SPIRIT", INCENSE_BURNER, 1, 1),
                item("SPIRIT", MAPAE, 2, 2),
                item("BUILDING", buildingId, 3, 3)))
                .andExpect(status().isOk());

        // 마패만 남기고 위치를 옮긴다 — 대향로와 건물은 내려간다.
        put(player, "/api/exhibition/layout", layout(item("SPIRIT", MAPAE, 7, 8)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.spirits[0].artifactId").value(INCENSE_BURNER))
                .andExpect(jsonPath("$.data.spirits[0].displayed").value(false))
                .andExpect(jsonPath("$.data.spirits[0].position").doesNotExist())
                .andExpect(jsonPath("$.data.spirits[1].displayed").value(true))
                .andExpect(jsonPath("$.data.spirits[1].position.x").value(7))
                .andExpect(jsonPath("$.data.spirits[1].position.y").value(8))
                .andExpect(jsonPath("$.data.buildings[0].displayed").value(false))
                .andExpect(jsonPath("$.data.buildings[0].position").doesNotExist());

        // 빈 배열은 전부 내리겠다는 뜻이다.
        put(player, "/api/exhibition/layout", layout())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.spirits[1].displayed").value(false));
        assertThat(userArtifactRepository.findAllByUserId(player.id()))
                .noneMatch(UserArtifact::isDisplayed);
        assertThat(userBuildingRepository.findAllByUserId(player.id()))
                .noneMatch(UserBuilding::isDisplayed);
    }

    @Test
    @DisplayName("둘 수 없는 항목이 하나라도 있으면 E001 이고 아무것도 바뀌지 않는다")
    void 둘_수_없는_항목() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        give(player, GEUMGWAN);           // 작업 중
        int unownedBuilding = catalog.buildingOf(Era.JOSEON).orElseThrow().getId();

        // 둘 수 있는 대향로가 앞에 있어도 함께 거절된다.
        put(player, "/api/exhibition/layout", layout(
                item("SPIRIT", INCENSE_BURNER, 1, 1),
                item("SPIRIT", GEUMGWAN, 2, 2),
                item("SPIRIT", MAPAE, 3, 3),
                item("BUILDING", unownedBuilding, 4, 4)))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("E001"))
                .andExpect(jsonPath("$.errors.length()").value(3))
                .andExpect(jsonPath("$.errors[0].field").value("items[1]"))
                .andExpect(jsonPath("$.errors[1].field").value("items[2]"))
                .andExpect(jsonPath("$.errors[2].field").value("items[3]"));

        assertThat(userArtifactRepository.findByUserIdAndArtifactId(player.id(), INCENSE_BURNER)
                .orElseThrow().isDisplayed()).isFalse();
    }

    @Test
    @DisplayName("남의 건물과 도깨비도 내 전시관에는 둘 수 없다")
    void 남의_것() throws Exception {
        Player player = player("학예사");
        Player other = player("이웃");
        giveCompleted(other, INCENSE_BURNER);
        int othersBuilding = giveBuilding(other, Era.GORYEO);

        put(player, "/api/exhibition/layout", layout(item("SPIRIT", INCENSE_BURNER, 1, 1)))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("E001"));
        put(player, "/api/exhibition/layout", layout(item("BUILDING", othersBuilding, 1, 1)))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("E001"));

        assertThat(userArtifactRepository.findByUserIdAndArtifactId(other.id(), INCENSE_BURNER)
                .orElseThrow().isDisplayed()).isFalse();
    }

    @Test
    @DisplayName("좌표가 그리드 밖이면 항목별 사유와 함께 C001 이다")
    void 좌표_범위() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        giveCompleted(player, MAPAE);

        put(player, "/api/exhibition/layout", layout(
                item("SPIRIT", INCENSE_BURNER, 20, 5),
                item("SPIRIT", MAPAE, 5, -1)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"))
                .andExpect(jsonPath("$.errors.length()").value(2))
                .andExpect(jsonPath("$.errors[0].field").value("items[0].x"))
                .andExpect(jsonPath("$.errors[0].reason").value("0 이상 20 미만이어야 합니다."))
                .andExpect(jsonPath("$.errors[1].field").value("items[1].y"));

        assertThat(userArtifactRepository.findAllByUserId(player.id())).noneMatch(UserArtifact::isDisplayed);
    }

    @Test
    @DisplayName("같은 항목이 두 번 들어 있으면 C001 이다")
    void 중복_항목() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);

        put(player, "/api/exhibition/layout", layout(
                item("SPIRIT", INCENSE_BURNER, 1, 1),
                item("SPIRIT", INCENSE_BURNER, 2, 2)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"))
                .andExpect(jsonPath("$.errors[0].field").value("items[1]"));
    }

    @Test
    @DisplayName("같은 번호라도 도깨비와 건물은 다른 항목이다")
    void 번호가_같은_도깨비와_건물() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, 1);
        int buildingId = giveBuilding(player, Era.THREE_KINGDOMS);
        assertThat(buildingId).isEqualTo(1);

        put(player, "/api/exhibition/layout", layout(
                item("SPIRIT", 1, 1, 1),
                item("BUILDING", buildingId, 2, 2)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.spirits[0].displayed").value(true))
                .andExpect(jsonPath("$.data.buildings[0].displayed").value(true));
    }

    @Test
    @DisplayName("필드가 빠지거나 값이 틀리면 C001 이다")
    void 입력_검증() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);

        // 좌표가 빠지면 0 이 되어 한 칸에 쌓이지 않고 거절된다.
        put(player, "/api/exhibition/layout", "{\"items\":[{\"type\":\"SPIRIT\",\"id\":2,\"y\":3}]}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"))
                .andExpect(jsonPath("$.errors[0].field").value("items[0].x"));
        put(player, "/api/exhibition/layout", "{\"items\":[{\"id\":2,\"x\":1,\"y\":3}]}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.errors[0].field").value("items[0].type"));
        put(player, "/api/exhibition/layout", "{}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.errors[0].field").value("items"));
        put(player, "/api/exhibition/layout", "{\"items\":[{\"type\":\"CASTLE\",\"id\":2,\"x\":1,\"y\":3}]}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
    }

    @Test
    @DisplayName("도깨비를 숨기면 목록에는 남고 좌표는 그대로이며, 다시 보이면 돌아온다")
    void 숨기기() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        put(player, "/api/exhibition/layout", layout(item("SPIRIT", INCENSE_BURNER, 6, 11)))
                .andExpect(status().isOk());

        patch(player, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":true}", INCENSE_BURNER)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("SPIRIT_VISIBILITY_UPDATED"))
                .andExpect(jsonPath("$.data.artifactId").value(INCENSE_BURNER))
                .andExpect(jsonPath("$.data.hidden").value(true));

        get(player, "/api/exhibition")
                .andExpect(jsonPath("$.data.spirits[0].hidden").value(true))
                .andExpect(jsonPath("$.data.spirits[0].displayed").value(true))
                .andExpect(jsonPath("$.data.spirits[0].position.x").value(6))
                .andExpect(jsonPath("$.data.spirits[0].position.y").value(11));
        get(player, "/api/artifacts/{id}", INCENSE_BURNER)
                .andExpect(jsonPath("$.data.hidden").value(true));

        // 같은 값으로 다시 보내도 200 이다.
        patch(player, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":true}", INCENSE_BURNER)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.hidden").value(true));

        patch(player, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":false}", INCENSE_BURNER)
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.hidden").value(false));
        get(player, "/api/exhibition")
                .andExpect(jsonPath("$.data.spirits[0].hidden").value(false))
                .andExpect(jsonPath("$.data.spirits[0].position.x").value(6));
    }

    @Test
    @DisplayName("배치를 저장해도 숨김은 바뀌지 않는다")
    void 배치는_숨김을_바꾸지_않는다() throws Exception {
        Player player = player("학예사");
        giveCompleted(player, INCENSE_BURNER);
        patch(player, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":true}", INCENSE_BURNER)
                .andExpect(status().isOk());

        put(player, "/api/exhibition/layout", layout(item("SPIRIT", INCENSE_BURNER, 1, 1)))
                .andExpect(jsonPath("$.data.spirits[0].hidden").value(true));
        put(player, "/api/exhibition/layout", layout())
                .andExpect(jsonPath("$.data.spirits[0].hidden").value(true))
                .andExpect(jsonPath("$.data.spirits[0].displayed").value(false));
    }

    @Test
    @DisplayName("숨기기는 보유하지 않은 유물이면 W001, 정령 수호 전이면 W002 다")
    void 숨기기_실패() throws Exception {
        Player player = player("학예사");
        give(player, GEUMGWAN);

        patch(player, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":true}", MAPAE)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("W001"));
        patch(player, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":true}", 999)
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("W001"));
        patch(player, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":true}", GEUMGWAN)
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("W002"));
        // 값이 빠지면 숨김이 풀리는 사고가 나지 않게 거절한다.
        patch(player, "/api/exhibition/spirits/{id}/visibility", "{}", INCENSE_BURNER)
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"))
                .andExpect(jsonPath("$.errors[0].field").value("hidden"));
    }

    @Test
    @DisplayName("친구에게는 배치하고 숨기지 않은 것만, 선명도 없이 보인다")
    void 친구_전시관() throws Exception {
        Player owner = player("주인");
        Player visitor = player("방문객");
        friendshipRepository.save(Friendship.between(owner.id(), visitor.id()));
        giveCompleted(owner, INCENSE_BURNER);
        giveCompleted(owner, GEUMGWAN);   // 배치하지 않는다
        giveCompleted(owner, MAPAE);      // 배치하고 숨긴다
        int buildingId = giveBuilding(owner, Era.GORYEO);
        patch(owner, "/api/artifacts/{id}/spirit-name", "{\"name\":\"향로지기\"}", INCENSE_BURNER)
                .andExpect(status().isOk());

        put(owner, "/api/exhibition/layout", layout(
                item("SPIRIT", INCENSE_BURNER, 6, 11),
                item("SPIRIT", MAPAE, 9, 9),
                item("BUILDING", buildingId, 10, 4)))
                .andExpect(status().isOk());
        patch(owner, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":true}", MAPAE)
                .andExpect(status().isOk());

        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.grid.width").value(20))
                .andExpect(jsonPath("$.data.spirits.length()").value(1))
                .andExpect(jsonPath("$.data.spirits[0].artifactId").value(INCENSE_BURNER))
                .andExpect(jsonPath("$.data.spirits[0].revealLevel").value("NAMED"))
                .andExpect(jsonPath("$.data.spirits[0].spiritName").value("향로지기"))
                .andExpect(jsonPath("$.data.spirits[0].realName").value("금동대향로"))
                .andExpect(jsonPath("$.data.spirits[0].awakeningStage").value("AWAKENED"))
                .andExpect(jsonPath("$.data.spirits[0].position.x").value(6))
                .andExpect(jsonPath("$.data.spirits[0].position.y").value(11))
                .andExpect(jsonPath("$.data.spirits[0].clarity").doesNotExist())
                .andExpect(jsonPath("$.data.buildings.length()").value(1))
                .andExpect(jsonPath("$.data.buildings[0].buildingId").value(buildingId))
                .andExpect(jsonPath("$.data.buildings[0].position.x").value(10))
                .andExpect(jsonPath("$.data.capturedAt").exists());
    }

    @Test
    @DisplayName("방금 숨긴 도깨비는 접속 기록(1분)을 기다리지 않고 친구에게서 바로 빠진다")
    void 숨기면_친구에게서_바로_빠진다() throws Exception {
        Player owner = player("주인");
        Player visitor = player("방문객");
        friendshipRepository.save(Friendship.between(owner.id(), visitor.id()));
        giveCompleted(owner, INCENSE_BURNER);
        put(owner, "/api/exhibition/layout", layout(item("SPIRIT", INCENSE_BURNER, 6, 11)))
                .andExpect(status().isOk());
        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(jsonPath("$.data.spirits.length()").value(1));

        // 시계를 옮기지 않았으므로 이 요청의 접속 기록은 1분 간격에 걸려 스냅샷을 찍지 않는다.
        patch(owner, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":true}", INCENSE_BURNER)
                .andExpect(status().isOk());
        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(jsonPath("$.data.spirits").isEmpty());

        patch(owner, "/api/exhibition/spirits/{id}/visibility", "{\"hidden\":false}", INCENSE_BURNER)
                .andExpect(status().isOk());
        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(jsonPath("$.data.spirits.length()").value(1));

        put(owner, "/api/exhibition/layout", layout())
                .andExpect(status().isOk());
        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(jsonPath("$.data.spirits").isEmpty());
    }

    @Test
    @DisplayName("항목 자리에 null 이 들어 있으면 C001 이다")
    void null_항목() throws Exception {
        Player player = player("학예사");

        put(player, "/api/exhibition/layout", "{\"items\":[null]}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
    }

    @Test
    @DisplayName("배치를 저장해도 같은 사용자의 건물 강화로 올라간 레벨을 옛 값으로 덮어쓰지 않는다")
    void 배치_저장이_레벨을_덮어쓰지_않는다() {
        Player player = player("학예사");
        int buildingId = giveBuilding(player, Era.GORYEO);
        TransactionTemplate otherTransaction = new TransactionTemplate(transactionManager);
        otherTransaction.setPropagationBehavior(TransactionDefinition.PROPAGATION_REQUIRES_NEW);

        transactionTemplate.executeWithoutResult(status -> {
            // 이 트랜잭션이 레벨 1 로 읽어 둔 사이에
            UserBuilding stale = userBuildingRepository.findByUserIdAndBuildingId(player.id(), buildingId)
                    .orElseThrow();
            assertThat(stale.getLevel()).isEqualTo(1);
            // 다른 트랜잭션이 레벨을 2 로 올려 커밋하고
            otherTransaction.executeWithoutResult(other -> userBuildingRepository
                    .findByUserIdAndBuildingId(player.id(), buildingId).orElseThrow().levelUp());
            // 옛 값을 쥔 쪽이 배치만 바꿔 저장한다.
            stale.placeAt(3, 4);
        });

        UserBuilding saved = userBuildingRepository.findByUserIdAndBuildingId(player.id(), buildingId)
                .orElseThrow();
        assertThat(saved.getLevel()).isEqualTo(2);
        assertThat(saved.isDisplayed()).isTrue();
        assertThat(saved.getPosX()).isEqualTo(3);
    }

    @Test
    @DisplayName("강화한 건물의 레벨은 배치를 저장한 뒤에도 그대로이고 친구에게도 그 레벨로 보인다")
    void 강화_후_배치() throws Exception {
        Player owner = player("주인");
        Player visitor = player("방문객");
        friendshipRepository.save(Friendship.between(owner.id(), visitor.id()));
        int buildingId = giveBuilding(owner, Era.GORYEO);
        transactionTemplate.executeWithoutResult(status -> wallet.add(owner.id(), Era.GORYEO, 100));
        post(owner, "/api/buildings/{id}/level-up", "{\"targetLevel\":2}", buildingId)
                .andExpect(status().isOk());

        put(owner, "/api/exhibition/layout", layout(item("BUILDING", buildingId, 10, 4)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.buildings[0].level").value(2));
        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(jsonPath("$.data.buildings[0].level").value(2));
    }

    @Test
    @DisplayName("스냅샷 갱신은 같은 사용자의 다른 트랜잭션이 사용자 행을 잡고 있는 동안 기다린다")
    void 스냅샷_갱신은_사용자_행_락을_기다린다() throws Exception {
        Player owner = player("주인");
        CountDownLatch locked = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        ExecutorService pool = Executors.newFixedThreadPool(2);
        try {
            // 같은 사용자의 저장(사용자 행을 첫 읽기로 잠그고 진행 중인 트랜잭션)을 흉내 낸다.
            Future<?> holder = pool.submit(() -> transactionTemplate.executeWithoutResult(status -> {
                userRepository.lockAllByIds(List.of(owner.id()));
                locked.countDown();
                try {
                    release.await(10, TimeUnit.SECONDS);
                } catch (InterruptedException interrupted) {
                    Thread.currentThread().interrupt();
                }
            }));
            assertThat(locked.await(10, TimeUnit.SECONDS)).isTrue();

            Future<?> capture = pool.submit(() -> snapshotService.captureOnActivity(
                    new UserActivityRecordedEvent(owner.id(), LocalDateTime.now(clock))));
            // 잠금이 풀리기 전에는 끝나지 않고, 스냅샷도 아직 없다.
            assertThatThrownBy(() -> capture.get(600, TimeUnit.MILLISECONDS)).isInstanceOf(TimeoutException.class);
            assertThat(snapshotService.find(owner.id())).isEmpty();

            release.countDown();
            holder.get(10, TimeUnit.SECONDS);
            capture.get(10, TimeUnit.SECONDS);
            assertThat(snapshotService.find(owner.id())).isPresent();
        } finally {
            release.countDown();
            pool.shutdownNow();
        }
    }

    @Test
    @DisplayName("같은 사용자의 저장·숨기기·접속이 동시에 몰려도 친구에게 보이는 모습은 끝내 실제 배치와 같다")
    void 동시_요청_뒤_스냅샷_일치() throws Exception {
        Player owner = player("주인");
        giveCompleted(owner, INCENSE_BURNER);
        giveCompleted(owner, MAPAE);
        int buildingId = giveBuilding(owner, Era.GORYEO);
        String layoutA = layout(item("SPIRIT", INCENSE_BURNER, 1, 1), item("BUILDING", buildingId, 5, 5));
        String layoutB = layout(item("SPIRIT", MAPAE, 7, 7), item("SPIRIT", INCENSE_BURNER, 2, 3));

        ExecutorService pool = Executors.newFixedThreadPool(4);
        try {
            for (int round = 0; round < 15; round++) {
                // 접속 기록이 1분에 한 번이라, 매 판 시계를 옮겨야 모든 판에서 접속 기록 경로가 스냅샷을 찍는다.
                clock.advance(Duration.ofMinutes(2));
                int shift = round % 5;
                List<Callable<Integer>> operations = List.of(
                        () -> put(owner, "/api/exhibition/layout", layoutA).andReturn().getResponse().getStatus(),
                        () -> put(owner, "/api/exhibition/layout", layoutB).andReturn().getResponse().getStatus(),
                        () -> patch(owner, "/api/exhibition/spirits/{id}/visibility",
                                "{\"hidden\":" + (shift % 2 == 0) + "}", INCENSE_BURNER)
                                .andReturn().getResponse().getStatus(),
                        () -> get(owner, "/api/workbench").andReturn().getResponse().getStatus());

                List<Future<Integer>> results = new ArrayList<>();
                for (Callable<Integer> operation : operations) {
                    results.add(pool.submit(operation));
                }
                for (Future<Integer> result : results) {
                    assertThat(result.get()).as("%d 번째 판의 응답", round).isEqualTo(200);
                }

                ExhibitionView expected = transactionTemplate.execute(
                        status -> viewProvider.readPublicView(owner.id()));
                ExhibitionView shown = snapshotService.find(owner.id()).orElseThrow().view();
                assertThat(shown).as("%d 번째 판 뒤의 스냅샷", round).isEqualTo(expected);
            }
        } finally {
            pool.shutdownNow();
        }
    }

    @Test
    @DisplayName("인증 없이는 전시관을 볼 수도 고칠 수도 없다")
    void 인증_필요() throws Exception {
        mockMvc.perform(MockMvcRequestBuilders.get("/api/exhibition"))
                .andExpect(status().isUnauthorized());
        mockMvc.perform(MockMvcRequestBuilders.put("/api/exhibition/layout")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(layout()))
                .andExpect(status().isUnauthorized());
    }

    /** 시대 유물 3종을 돌리지 않고 건물을 쥐여 준다. 지급 자체는 {@code CoreLoopFlowTest} 가 본다. */
    private int giveBuilding(Player player, Era era) {
        int buildingId = catalog.buildingOf(era).orElseThrow().getId();
        userBuildingRepository.save(UserBuilding.awarded(player.id(), buildingId, LocalDateTime.now(clock)));
        return buildingId;
    }

    /** 강화 확정은 게이지를 채워야 해서 단계를 곧바로 맞춘다. */
    private void setAwakening(UserArtifact artifact, AwakeningStage stage) {
        transactionTemplate.executeWithoutResult(status -> {
            UserArtifact managed = userArtifactRepository.findById(artifact.getId()).orElseThrow();
            while (managed.getAwakeningStage() != stage) {
                managed.awaken(0);
            }
        });
    }

    private static String item(String type, int id, int x, int y) {
        return "{\"type\":\"%s\",\"id\":%d,\"x\":%d,\"y\":%d}".formatted(type, id, x, y);
    }

    private static String layout(String... items) {
        return "{\"items\":[" + String.join(",", items) + "]}";
    }
}
