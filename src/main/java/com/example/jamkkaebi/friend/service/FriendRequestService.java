package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.config.FriendProperties;
import com.example.jamkkaebi.friend.domain.FriendRecommendation;
import com.example.jamkkaebi.friend.domain.FriendRelation;
import com.example.jamkkaebi.friend.domain.FriendRequest;
import com.example.jamkkaebi.friend.domain.FriendRequestStatus;
import com.example.jamkkaebi.friend.domain.Friendship;
import com.example.jamkkaebi.friend.domain.RequestDirection;
import com.example.jamkkaebi.friend.dto.response.FriendRequestCancelResponse;
import com.example.jamkkaebi.friend.dto.response.FriendRequestListResponse;
import com.example.jamkkaebi.friend.dto.response.FriendRequestResponse;
import com.example.jamkkaebi.friend.repository.FriendRecommendationRepository;
import com.example.jamkkaebi.friend.repository.FriendRequestRepository;
import com.example.jamkkaebi.friend.repository.FriendshipRepository;
import com.example.jamkkaebi.friend.spi.PlayerCollection;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.user.domain.User;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDateTime;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * 친구 요청 — 보내기 · 요청함 · 수락 · 거절 · 취소.
 *
 * <p>상한(친구 30 · 받은 요청 20 · 보낸 요청 10)은 세어 보고 넣는 방식이라 동시 요청에 약하다. 그래서 상태를
 * 바꾸는 작업은 모두 <b>관련된 두 사용자 행을 먼저 잠그고</b> 센다.
 */
@Service
public class FriendRequestService {

    private final FriendUsers friendUsers;
    private final SocialGraph socialGraph;
    private final FriendshipRepository friendshipRepository;
    private final FriendRequestRepository friendRequestRepository;
    private final FriendRecommendationRepository recommendationRepository;
    private final PlayerCardAssembler playerCardAssembler;
    private final FriendCodeLookupLimiter lookupLimiter;
    private final FriendProperties properties;
    private final GameClock gameClock;

    public FriendRequestService(FriendUsers friendUsers,
                                SocialGraph socialGraph,
                                FriendshipRepository friendshipRepository,
                                FriendRequestRepository friendRequestRepository,
                                FriendRecommendationRepository recommendationRepository,
                                PlayerCardAssembler playerCardAssembler,
                                FriendCodeLookupLimiter lookupLimiter,
                                FriendProperties properties,
                                GameClock gameClock) {
        this.friendUsers = friendUsers;
        this.socialGraph = socialGraph;
        this.friendshipRepository = friendshipRepository;
        this.friendRequestRepository = friendRequestRepository;
        this.recommendationRepository = recommendationRepository;
        this.playerCardAssembler = playerCardAssembler;
        this.lookupLimiter = lookupLimiter;
        this.properties = properties;
        this.gameClock = gameClock;
    }

    /**
     * 친구 요청 결과.
     *
     * @param friendAdded 상대가 이미 나에게 요청해 둔 상태라 바로 친구가 됐는지
     * @param response    응답 본문
     */
    public record SendResult(boolean friendAdded, FriendRequestResponse response) {
    }

    /**
     * 친구 코드로 요청을 보낸다.
     *
     * <p>상대가 나에게 보낸 요청이 있다면 뒤에 도착한 이 요청으로 <b>자동 수락</b>한다.
     * 내가 예전에 거절한 상대의 요청도 여기에 포함된다
     *
     */
    @Transactional
    public SendResult send(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        lookupLimiter.acquire(me.getId());
        String code = FriendUsers.requireFormat(rawFriendCode);
        if (code.equals(me.getFriendCode())) {
            throw FriendUsers.invalidFriendCode("자기 자신에게는 요청할 수 없습니다.");
        }
        User target = friendUsers.findByCode(code)
                .orElseThrow(() -> new BusinessException(ErrorCode.FRIEND_CODE_NOT_FOUND));

        friendUsers.lock(me.getId(), target.getId());
        if (socialGraph.blockedEitherWay(me.getId(), target.getId())) {
            throw new BusinessException(ErrorCode.FRIEND_CODE_NOT_FOUND);
        }
        if (socialGraph.areFriends(me.getId(), target.getId())) {
            throw new BusinessException(ErrorCode.ALREADY_FRIENDS);
        }

        LocalDateTime now = gameClock.now();
        Optional<FriendRequest> mine =
                friendRequestRepository.findByFromUserIdAndToUserId(me.getId(), target.getId());
        if (mine.isPresent() && mine.get().isOutstanding(now)) {
            markRecommendationRequested(me, target);
            return new SendResult(false,
                    FriendRequestResponse.sent(playerCardAssembler.card(target), mine.get().getExpiresAt()));
        }

        requireRoomForFriendship(me, target);

        boolean crossing = friendRequestRepository.findByFromUserIdAndToUserId(target.getId(), me.getId())
                .filter(request -> request.isOutstanding(now))
                .isPresent();
        if (crossing) {
            becomeFriends(me, target);
            markRecommendationRequested(me, target);
            return new SendResult(true, FriendRequestResponse.friend(playerCardAssembler.card(target)));
        }

        if (friendRequestRepository.countOutstandingSent(me.getId(), now) >= properties.sentRequestLimit()) {
            throw new BusinessException(ErrorCode.SENT_REQUEST_LIMIT_REACHED);
        }
        if (friendRequestRepository.countAwaiting(target.getId(), FriendRequestStatus.PENDING, now)
                >= properties.receivedRequestLimit()) {
            throw new BusinessException(ErrorCode.RECEIVER_INBOX_FULL);
        }

        // 같은 방향의 만료된 요청이 남아 있으면 유니크 제약에 걸리므로 먼저 지운다.
        mine.ifPresent(expired -> {
            friendRequestRepository.delete(expired);
            friendRequestRepository.flush();
        });
        FriendRequest request = friendRequestRepository.save(FriendRequest.builder()
                .fromUserId(me.getId())
                .toUserId(target.getId())
                .requestedAt(now)
                .expiresAt(now.plus(properties.requestTtl()))
                .build());
        markRecommendationRequested(me, target);
        return new SendResult(false,
                FriendRequestResponse.sent(playerCardAssembler.card(target), request.getExpiresAt()));
    }

    /**
     * 요청함을 조회한다.
     *
     * <p>상대가 거절한 요청도 만료 전까지 그대로 보인다.
     */
    @Transactional(readOnly = true)
    public FriendRequestListResponse list(String myFriendCode, RequestDirection direction) {
        User me = friendUsers.me(myFriendCode);
        LocalDateTime now = gameClock.now();
        boolean received = direction == RequestDirection.RECEIVED;

        List<FriendRequest> requests = received
                ? friendRequestRepository.findAwaitingReceived(me.getId(), FriendRequestStatus.PENDING, now)
                : friendRequestRepository.findOutstandingSent(me.getId(), now);
        List<Long> counterpartIds = requests.stream()
                .map(request -> counterpartOf(request, received))
                .toList();
        Map<Long, User> counterparts = friendUsers.findAll(counterpartIds);
        Map<Long, PlayerCollection> collections = playerCardAssembler.collectionsOf(counterpartIds);

        List<FriendRequestListResponse.RequestItem> items = requests.stream()
                .filter(request -> counterparts.containsKey(counterpartOf(request, received)))
                .map(request -> new FriendRequestListResponse.RequestItem(
                        playerCardAssembler.card(counterparts.get(counterpartOf(request, received)), collections),
                        request.getRequestedAt(),
                        request.getExpiresAt()))
                .toList();

        int limit = received ? properties.receivedRequestLimit() : properties.sentRequestLimit();
        return new FriendRequestListResponse(direction, items.size(), limit, items);
    }

    // 받은 요청 수락
    @Transactional
    public FriendRequestResponse accept(String myFriendCode, String rawSenderCode) {
        User me = friendUsers.me(myFriendCode);
        User sender = friendUsers.findByCode(rawSenderCode)
                .filter(other -> !other.getId().equals(me.getId()))
                .orElseThrow(() -> new BusinessException(ErrorCode.FRIEND_REQUEST_NOT_FOUND));

        friendUsers.lock(me.getId(), sender.getId());
        if (socialGraph.areFriends(me.getId(), sender.getId())) {
            return FriendRequestResponse.friend(playerCardAssembler.card(sender));
        }

        LocalDateTime now = gameClock.now();
        boolean awaiting = friendRequestRepository.findByFromUserIdAndToUserId(sender.getId(), me.getId())
                .filter(request -> request.isAwaitingResponse(now))
                .isPresent();
        if (!awaiting || socialGraph.blockedEitherWay(me.getId(), sender.getId())) {
            throw new BusinessException(ErrorCode.FRIEND_REQUEST_NOT_FOUND);
        }

        requireRoomForFriendship(me, sender);
        becomeFriends(me, sender);
        return FriendRequestResponse.friend(playerCardAssembler.card(sender));
    }

    // 받은 요청 거절
    @Transactional
    public void reject(String myFriendCode, String rawSenderCode) {
        User me = friendUsers.me(myFriendCode);
        LocalDateTime now = gameClock.now();
        friendUsers.findByCode(rawSenderCode)
                .flatMap(sender -> friendRequestRepository.findByFromUserIdAndToUserId(sender.getId(), me.getId()))
                .filter(request -> request.isAwaitingResponse(now))
                .ifPresent(FriendRequest::reject);
    }

    // 보낸 요청 취소
    @Transactional
    public FriendRequestCancelResponse cancel(String myFriendCode, String rawTargetCode) {
        User me = friendUsers.me(myFriendCode);
        String code = FriendUsers.requireFormat(rawTargetCode);
        if (code.equals(me.getFriendCode())) {
            throw FriendUsers.invalidFriendCode("자기 자신에게 보낸 요청은 없습니다.");
        }

        LocalDateTime now = gameClock.now();
        FriendRelation relation = FriendRelation.NONE;
        Optional<User> target = friendUsers.findByCode(code);
        if (target.isPresent()) {
            Long targetId = target.get().getId();
            friendUsers.lock(me.getId(), targetId);
            // 만료된 행도 함께 치운다. 다시 보낼 때 유니크 제약 때문에 지워야 하는 행이다.
            friendRequestRepository.findByFromUserIdAndToUserId(me.getId(), targetId)
                    .ifPresent(request -> {
                        friendRequestRepository.delete(request);
                        friendRequestRepository.flush();
                    });
            relation = socialGraph.relation(me.getId(), targetId, now);
        }

        long outstanding = friendRequestRepository.countOutstandingSent(me.getId(), now);
        return new FriendRequestCancelResponse(relation, (int) outstanding, properties.sentRequestLimit());
    }

    // 요청함에서 보이는 상대 — 받은 요청함이면 보낸 사람, 보낸 요청함이면 받은 사람.
    private static Long counterpartOf(FriendRequest request, boolean received) {
        return received ? request.getFromUserId() : request.getToUserId();
    }

    private void requireRoomForFriendship(User me, User other) {
        if (socialGraph.friendCountOf(me.getId()) >= properties.friendLimit()) {
            throw new BusinessException(ErrorCode.FRIEND_LIMIT_REACHED);
        }
        if (socialGraph.friendCountOf(other.getId()) >= properties.friendLimit()) {
            throw new BusinessException(ErrorCode.TARGET_FRIEND_LIMIT_REACHED);
        }
    }

    // 친구 관계 생성
    private void becomeFriends(User me, User other) {
        friendRequestRepository.deleteBetween(me.getId(), other.getId());
        friendshipRepository.save(Friendship.between(me.getId(), other.getId()));
    }

    // 오늘 추천받은 사람에게 요청했다면 추천 카드에 표시한다.
    private void markRecommendationRequested(User me, User target) {
        recommendationRepository
                .findByUserIdAndRecommendedUserIdAndRecommendedDate(me.getId(), target.getId(), gameClock.today())
                .ifPresent(FriendRecommendation::markRequested);
    }
}
