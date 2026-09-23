package com.example.jamkkaebi.coreloop;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.Difficulty;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.repository.UserArtifactRepository;
import com.example.jamkkaebi.artifact.repository.UserBuildingRepository;
import com.example.jamkkaebi.artifact.repository.UserEraCrystalRepository;
import com.example.jamkkaebi.artifact.service.ArtifactCatalog;
import com.example.jamkkaebi.artifact.service.MasterDataSeeder;
import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.box.repository.BoxOpenLogRepository;
import com.example.jamkkaebi.box.repository.GachaPityRepository;
import com.example.jamkkaebi.friend.repository.GiftLogRepository;
import com.example.jamkkaebi.global.security.JwtProvider;
import com.example.jamkkaebi.purification.repository.PlaySessionRepository;
import com.example.jamkkaebi.support.CoreLoopTestConfig;
import com.example.jamkkaebi.support.MutableClock;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import com.example.jamkkaebi.user.service.UserRegistrationService;
import com.jayway.jsonpath.JsonPath;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.ResultActions;
import org.springframework.test.web.servlet.request.MockMvcRequestBuilders;
import org.springframework.test.web.servlet.result.MockMvcResultMatchers;
import org.springframework.transaction.support.TransactionTemplate;

import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.LocalDateTime;
import java.util.List;
import java.util.UUID;

/**
 * 코어 루프 통합 테스트의 공통 준비물 — 사용자 만들기, 인증된 요청, 시계, 정리.
 */
@SpringBootTest
@AutoConfigureMockMvc
@Import(CoreLoopTestConfig.class)
abstract class CoreLoopApiTestSupport {

    protected static final LocalDateTime START = LocalDateTime.of(2026, 9, 12, 10, 0);

    @Autowired
    protected MockMvc mockMvc;
    @Autowired
    protected MutableClock clock;
    @Autowired
    protected UserRepository userRepository;
    @Autowired
    protected UserArtifactRepository userArtifactRepository;
    @Autowired
    protected UserBuildingRepository userBuildingRepository;
    @Autowired
    protected UserEraCrystalRepository crystalRepository;
    @Autowired
    protected BoxOpenLogRepository boxOpenLogRepository;
    @Autowired
    protected GachaPityRepository gachaPityRepository;
    @Autowired
    protected PlaySessionRepository playSessionRepository;
    @Autowired
    protected GiftLogRepository giftLogRepository;
    @Autowired
    protected ArtifactCatalog catalog;
    @Autowired
    protected TransactionTemplate transactionTemplate;
    @Autowired
    private MasterDataSeeder masterDataSeeder;
    @Autowired
    private UserRegistrationService userRegistrationService;
    @Autowired
    private JwtProvider jwtProvider;

    protected record Player(Long id, String code, String token) {
    }

    @BeforeEach
    void resetWorld() {
        clock.set(START);
        // 시딩은 덮어쓰기라 몇 번을 돌려도 결과가 같다. 기동 시점 실행 여부에 기대지 않는다.
        masterDataSeeder.seed();
    }

    @AfterEach
    void clearTables() {
        playSessionRepository.deleteAll();
        boxOpenLogRepository.deleteAll();
        gachaPityRepository.deleteAll();
        giftLogRepository.deleteAll();
        userArtifactRepository.deleteAll();
        userBuildingRepository.deleteAll();
        crystalRepository.deleteAll();
        userRepository.deleteAll();
    }

    protected Player player(String nickname) {
        User user = userRegistrationService
                .findOrRegister(AuthProvider.GOOGLE, "core-loop-test-" + UUID.randomUUID(), nickname)
                .user();
        return new Player(user.getId(), user.getFriendCode(), jwtProvider.createAccessToken(user.getFriendCode()));
    }

    /** 상자를 거치지 않고 유물을 쥐여 준다. 뽑기 결과에 기대지 않고 특정 유물을 다루기 위해서다. */
    protected UserArtifact give(Player player, int artifactId) {
        return userArtifactRepository.save(
                UserArtifact.acquired(player.id(), artifactId, LocalDateTime.now(clock)));
    }

    /**
     * 정령 수호까지 마친 유물을 쥐여 준다.
     *
     * <p>API 로 3페이즈를 돌리면 판당 요청 두 번이라 아홉 종을 채우는 데 스물일곱 판이 든다. 여기서
     * 확인하려는 것이 정화 자체가 아닐 때는 도메인의 진행 메서드를 직접 불러 같은 상태를 만든다.
     */
    protected UserArtifact giveCompleted(Player player, int artifactId) {
        LocalDateTime now = LocalDateTime.now(clock);
        UserArtifact userArtifact = UserArtifact.acquired(player.id(), artifactId, now);
        userArtifact.clearFirstPhase(now);
        userArtifact.clearFirstPhase(now);
        userArtifact.clearFirstPhase(now);
        return userArtifactRepository.save(userArtifact);
    }

    protected List<Integer> artifactIdsOf(Era era) {
        return catalog.allArtifacts().stream()
                .filter(artifact -> artifact.getEra() == era)
                .map(ArtifactMaster::getId)
                .toList();
    }

    /**
     * 정성을 쥐여 준다.
     *
     * <p>적립은 UPDATE 한 문장이라 트랜잭션 안에서만 돈다 — 테스트가 서비스 바깥에서 부르므로
     * 여기서 열어 준다.
     */
    protected void grantJeongseong(Player player, int amount) {
        transactionTemplate.executeWithoutResult(
                status -> userRepository.addJeongseong(player.id(), amount));
    }

    protected int jeongseongOf(Player player) {
        return userRepository.findById(player.id()).orElseThrow().getJeongseong();
    }

    protected int crystalOf(Player player, Era era) {
        return crystalRepository.findByUserIdAndEra(player.id(), era)
                .map(com.example.jamkkaebi.artifact.domain.UserEraCrystal::getCrystal)
                .orElse(0);
    }

    protected ResultActions get(Player player, String url, Object... uriVariables) throws Exception {
        return mockMvc.perform(MockMvcRequestBuilders.get(url, uriVariables)
                .header("Authorization", "Bearer " + player.token()));
    }

    protected ResultActions post(Player player, String url, String body, Object... uriVariables) throws Exception {
        return mockMvc.perform(MockMvcRequestBuilders.post(url, uriVariables)
                .header("Authorization", "Bearer " + player.token())
                .contentType(MediaType.APPLICATION_JSON)
                .content(body));
    }

    protected ResultActions patch(Player player, String url, String body, Object... uriVariables) throws Exception {
        return mockMvc.perform(MockMvcRequestBuilders.patch(url, uriVariables)
                .header("Authorization", "Bearer " + player.token())
                .contentType(MediaType.APPLICATION_JSON)
                .content(body));
    }

    protected <T> T read(ResultActions result, String jsonPath) throws Exception {
        return JsonPath.read(
                result.andReturn().getResponse().getContentAsString(StandardCharsets.UTF_8), jsonPath);
    }

    /** 세션을 시작하고 토큰을 돌려준다. {@code difficulty} 가 {@code null} 이면 최초 정화다. */
    protected String startPurification(Player player, int artifactId, Difficulty difficulty) throws Exception {
        String body = difficulty == null
                ? "{\"artifactId\":%d}".formatted(artifactId)
                : "{\"artifactId\":%d,\"difficulty\":\"%s\"}".formatted(artifactId, difficulty);
        ResultActions started = post(player, "/api/purifications", body)
                .andExpect(MockMvcResultMatchers.status().isCreated());
        return read(started, "$.data.sessionToken");
    }

    /**
     * 한 판을 무손상으로 끝낸다.
     *
     * <p>시계를 1분 밀고 보고하는 이유는, 세션을 받자마자 결과를 보내면 최소 플레이 시간에 걸려
     * 거절되기 때문이다 — 그 검사가 살아 있는지도 함께 확인하는 셈이다.
     */
    protected ResultActions clearPhase(Player player, int artifactId, Difficulty difficulty) throws Exception {
        String token = startPurification(player, artifactId, difficulty);
        clock.advance(Duration.ofMinutes(1));
        return post(player, "/api/purifications/{token}/result", """
                {"result":"SUCCESS","tapsUsed":10,"scoutsUsed":1,"damage":0,
                 "bonusTiles":{"jeongseong":0,"eraCrystal":0}}
                """, token);
    }

    /** 최초 정화 3페이즈를 끝까지 — 유물 하나가 {@code COMPLETED} 가 된다. */
    protected ResultActions completeFirstPurification(Player player, int artifactId) throws Exception {
        clearPhase(player, artifactId, null);
        clearPhase(player, artifactId, null);
        return clearPhase(player, artifactId, null);
    }
}
