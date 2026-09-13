package com.example.jamkkaebi.friend;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.friend.domain.FriendRequest;
import com.example.jamkkaebi.friend.domain.Friendship;
import com.example.jamkkaebi.friend.repository.BlockRepository;
import com.example.jamkkaebi.friend.repository.ExhibitionSnapshotRepository;
import com.example.jamkkaebi.friend.repository.FriendRecommendationRepository;
import com.example.jamkkaebi.friend.repository.FriendRequestRepository;
import com.example.jamkkaebi.friend.repository.FriendshipRepository;
import com.example.jamkkaebi.friend.repository.GiftLogRepository;
import com.example.jamkkaebi.friend.repository.VisitLogRepository;
import com.example.jamkkaebi.global.security.JwtProvider;
import com.example.jamkkaebi.support.FakeExhibitionViewReader;
import com.example.jamkkaebi.support.FriendTestConfig;
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

import java.nio.charset.StandardCharsets;
import java.time.LocalDateTime;
import java.util.UUID;

/**
 * 친구 기능 통합 테스트의 공통 준비물 — 사용자 만들기, 인증된 요청, 시계, 정리.
 */
@SpringBootTest
@AutoConfigureMockMvc
@Import(FriendTestConfig.class)
abstract class FriendApiTestSupport {

    protected static final LocalDateTime START = LocalDateTime.of(2026, 9, 12, 10, 0);

    @Autowired
    protected MockMvc mockMvc;
    @Autowired
    protected MutableClock clock;
    @Autowired
    protected FakeExhibitionViewReader exhibitionViewReader;
    @Autowired
    protected UserRepository userRepository;
    @Autowired
    protected FriendshipRepository friendshipRepository;
    @Autowired
    protected FriendRequestRepository friendRequestRepository;
    @Autowired
    protected BlockRepository blockRepository;
    @Autowired
    protected FriendRecommendationRepository recommendationRepository;
    @Autowired
    protected GiftLogRepository giftLogRepository;
    @Autowired
    protected VisitLogRepository visitLogRepository;
    @Autowired
    protected ExhibitionSnapshotRepository snapshotRepository;
    @Autowired
    private UserRegistrationService userRegistrationService;
    @Autowired
    private JwtProvider jwtProvider;

    /**
     * 테스트 속 사용자.
     *
     * @param id    내부 id (저장소 준비용)
     * @param code  친구 코드
     * @param token Access Token
     */
    protected record Player(Long id, String code, String token) {
    }

    @BeforeEach
    void resetWorld() {
        clock.set(START);
        exhibitionViewReader.clear();
    }

    @AfterEach
    void clearTables() {
        friendshipRepository.deleteAll();
        friendRequestRepository.deleteAll();
        blockRepository.deleteAll();
        recommendationRepository.deleteAll();
        giftLogRepository.deleteAll();
        visitLogRepository.deleteAll();
        snapshotRepository.deleteAll();
        userRepository.deleteAll();
    }

    protected Player player(String nickname) {
        User user = userRegistrationService
                .findOrRegister(AuthProvider.GOOGLE, "friend-test-" + UUID.randomUUID(), nickname)
                .user();
        return new Player(user.getId(), user.getFriendCode(), jwtProvider.createAccessToken(user.getFriendCode()));
    }

    protected void makeFriends(Player one, Player other) {
        friendshipRepository.save(Friendship.between(one.id(), other.id()));
    }

    /** 요청을 저장소에 바로 넣는다. 상한 테스트에서 상대 사용자를 실제로 만들지 않기 위해서다. */
    protected void saveRequest(Long fromUserId, Long toUserId) {
        LocalDateTime now = LocalDateTime.now(clock);
        friendRequestRepository.save(FriendRequest.builder()
                .fromUserId(fromUserId)
                .toUserId(toUserId)
                .requestedAt(now)
                .expiresAt(now.plusDays(14))
                .build());
    }

    protected int jeongseongOf(Player player) {
        return userRepository.findById(player.id()).orElseThrow().getJeongseong();
    }

    protected ResultActions get(Player player, String url, Object... uriVariables) throws Exception {
        return mockMvc.perform(MockMvcRequestBuilders.get(url, uriVariables)
                .header("Authorization", "Bearer " + player.token()));
    }

    protected ResultActions post(Player player, String url, Object... uriVariables) throws Exception {
        return mockMvc.perform(MockMvcRequestBuilders.post(url, uriVariables)
                .header("Authorization", "Bearer " + player.token()));
    }

    protected ResultActions delete(Player player, String url, Object... uriVariables) throws Exception {
        return mockMvc.perform(MockMvcRequestBuilders.delete(url, uriVariables)
                .header("Authorization", "Bearer " + player.token()));
    }

    protected ResultActions postCode(Player player, String url, String friendCode) throws Exception {
        return mockMvc.perform(MockMvcRequestBuilders.post(url)
                .header("Authorization", "Bearer " + player.token())
                .contentType(MediaType.APPLICATION_JSON)
                .content("""
                        {"friendCode":"%s"}
                        """.formatted(friendCode)));
    }

    protected ResultActions sendRequest(Player from, String friendCode) throws Exception {
        return postCode(from, "/api/friend-requests", friendCode);
    }

    protected <T> T read(ResultActions result, String jsonPath) throws Exception {
        return JsonPath.read(result.andReturn().getResponse().getContentAsString(StandardCharsets.UTF_8), jsonPath);
    }
}
