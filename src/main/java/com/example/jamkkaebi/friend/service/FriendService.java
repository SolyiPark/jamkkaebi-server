package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.config.FriendProperties;
import com.example.jamkkaebi.friend.domain.FriendRequestStatus;
import com.example.jamkkaebi.friend.domain.GiftState;
import com.example.jamkkaebi.friend.dto.response.FriendListResponse;
import com.example.jamkkaebi.friend.dto.response.FriendLookupResponse;
import com.example.jamkkaebi.friend.repository.FriendRequestRepository;
import com.example.jamkkaebi.friend.repository.FriendshipRepository;
import com.example.jamkkaebi.friend.repository.VisitLogRepository;
import com.example.jamkkaebi.friend.spi.PlayerCollection;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.user.domain.User;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.temporal.ChronoUnit;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * 친구 목록 · 친구 코드 조회 · 친구 삭제.
 */
@Service
public class FriendService {

    /** 최근 접속순. 접속 기록이 없는 사용자는 뒤로. */
    private static final Comparator<User> RECENTLY_ACTIVE_FIRST = Comparator
            .comparing(User::getLastActiveAt, Comparator.nullsLast(Comparator.reverseOrder()))
            .thenComparing(User::getId);

    private final FriendUsers friendUsers;
    private final SocialGraph socialGraph;
    private final FriendshipRepository friendshipRepository;
    private final FriendRequestRepository friendRequestRepository;
    private final VisitLogRepository visitLogRepository;
    private final GiftService giftService;
    private final PlayerCardAssembler playerCardAssembler;
    private final FriendCodeLookupLimiter lookupLimiter;
    private final FriendProperties properties;
    private final GameClock gameClock;

    public FriendService(FriendUsers friendUsers,
                         SocialGraph socialGraph,
                         FriendshipRepository friendshipRepository,
                         FriendRequestRepository friendRequestRepository,
                         VisitLogRepository visitLogRepository,
                         GiftService giftService,
                         PlayerCardAssembler playerCardAssembler,
                         FriendCodeLookupLimiter lookupLimiter,
                         FriendProperties properties,
                         GameClock gameClock) {
        this.friendUsers = friendUsers;
        this.socialGraph = socialGraph;
        this.friendshipRepository = friendshipRepository;
        this.friendRequestRepository = friendRequestRepository;
        this.visitLogRepository = visitLogRepository;
        this.giftService = giftService;
        this.playerCardAssembler = playerCardAssembler;
        this.lookupLimiter = lookupLimiter;
        this.properties = properties;
        this.gameClock = gameClock;
    }

    // 친구 정보 요약 
    @Transactional(readOnly = true)
    public FriendListResponse getFriends(String myFriendCode) {
        User me = friendUsers.me(myFriendCode);
        LocalDate today = gameClock.today();

        List<Long> friendIds = socialGraph.friendIdsOf(me.getId());
        Map<Long, User> friends = friendUsers.findAll(friendIds);
        Map<Long, PlayerCollection> collections = playerCardAssembler.collectionsOf(friendIds);
        Set<Long> visitedToday = new HashSet<>(visitLogRepository.findHostIdsVisited(me.getId(), today));
        Map<Long, GiftState> giftStates = giftService.giftStates(me.getId(), friendIds, today);

        List<FriendListResponse.FriendSummary> summaries = friends.values().stream()
                .sorted(RECENTLY_ACTIVE_FIRST)
                .map(friend -> new FriendListResponse.FriendSummary(
                        playerCardAssembler.card(friend, collections),
                        daysSinceLastActive(friend, today),
                        visitedToday.contains(friend.getId()),
                        giftStates.get(friend.getId())))
                .toList();

        FriendListResponse.Today todayStatus = new FriendListResponse.Today(
                visitLogRepository.existsByVisitorIdAndVisitDateAndRewardedTrue(me.getId(), today),
                giftService.sentToday(me.getId(), today),
                giftService.ticketAvailable(me.getId(), today));

        return new FriendListResponse(
                me.getFriendCode(),
                summaries.size(),
                properties.friendLimit(),
                summaries,
                friendRequestRepository.countAwaiting(
                        me.getId(), FriendRequestStatus.PENDING, gameClock.now()),
                todayStatus,
                gameClock.nextResetAt());
    }

    // 친구 코드 조회 
    @Transactional(readOnly = true)
    public FriendLookupResponse lookup(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        lookupLimiter.acquire(me.getId());
        String code = FriendUsers.requireFormat(rawFriendCode);

        User target = friendUsers.findByCode(code)
                .filter(found -> found.getId().equals(me.getId())
                        || !socialGraph.blockedEitherWay(me.getId(), found.getId()))
                .orElseThrow(() -> new BusinessException(ErrorCode.FRIEND_CODE_NOT_FOUND));

        Map<Long, PlayerCollection> collections = playerCardAssembler.collectionsOf(List.of(target.getId()));
        return new FriendLookupResponse(
                playerCardAssembler.card(target, collections),
                playerCardAssembler.restoredByEra(target, collections),
                socialGraph.relation(me.getId(), target.getId(), gameClock.now()));
    }

    // 친구 양방향 삭제 
    @Transactional
    public void removeFriend(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        friendUsers.findByCode(rawFriendCode)
                .flatMap(other -> friendshipRepository.findBetween(me.getId(), other.getId()))
                .ifPresent(friendshipRepository::delete);
    }

    private static long daysSinceLastActive(User user, LocalDate today) {
        LocalDateTime lastSeen = user.getLastActiveAt() != null ? user.getLastActiveAt() : user.getCreatedAt();
        if (lastSeen == null) {
            return 0;
        }
        return Math.max(0, ChronoUnit.DAYS.between(lastSeen.toLocalDate(), today));
    }
}
