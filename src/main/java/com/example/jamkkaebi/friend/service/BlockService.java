package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.domain.Block;
import com.example.jamkkaebi.friend.dto.response.BlockListResponse;
import com.example.jamkkaebi.friend.repository.BlockRepository;
import com.example.jamkkaebi.friend.repository.FriendRequestRepository;
import com.example.jamkkaebi.friend.repository.FriendshipRepository;
import com.example.jamkkaebi.user.domain.User;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * 차단 · 차단 목록 · 차단 해제.
 *
 */

@Service
public class BlockService {

    private final FriendUsers friendUsers;
    private final BlockRepository blockRepository;
    private final FriendshipRepository friendshipRepository;
    private final FriendRequestRepository friendRequestRepository;

    public BlockService(FriendUsers friendUsers,
                        BlockRepository blockRepository,
                        FriendshipRepository friendshipRepository,
                        FriendRequestRepository friendRequestRepository) {
        this.friendUsers = friendUsers;
        this.blockRepository = blockRepository;
        this.friendshipRepository = friendshipRepository;
        this.friendRequestRepository = friendRequestRepository;
    }

    //차단
    @Transactional
    public void block(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        String code = FriendUsers.requireFormat(rawFriendCode);
        if (code.equals(me.getFriendCode())) {
            throw FriendUsers.invalidFriendCode("자기 자신은 차단할 수 없습니다.");
        }
        Optional<User> target = friendUsers.findByCode(code);
        if (target.isEmpty()) {
            return;
        }

        Long targetId = target.get().getId();
        friendUsers.lock(me.getId(), targetId);
        if (!blockRepository.existsByBlockerIdAndBlockedId(me.getId(), targetId)) {
            blockRepository.save(Block.of(me.getId(), targetId));
        }
        friendshipRepository.findBetween(me.getId(), targetId).ifPresent(friendshipRepository::delete);
        friendRequestRepository.deleteBetween(me.getId(), targetId);
    }

    //차단 목록 조회
    @Transactional(readOnly = true)
    public BlockListResponse list(String myFriendCode) {
        User me = friendUsers.me(myFriendCode);
        List<Block> blocks = blockRepository.findAllByBlockerIdOrderByCreatedAtDescIdDesc(me.getId());
        Map<Long, User> blockedUsers = friendUsers.findAll(blocks.stream().map(Block::getBlockedId).toList());

        return new BlockListResponse(blocks.stream()
                .filter(block -> blockedUsers.containsKey(block.getBlockedId()))
                .map(block -> {
                    User blocked = blockedUsers.get(block.getBlockedId());
                    return new BlockListResponse.BlockedUser(
                            blocked.getFriendCode(), blocked.getNickname(), block.getCreatedAt());
                })
                .toList());
    }

    // 차단 해제. 이전 친구 관계·요청은 복구하지 않는다.
    @Transactional
    public void unblock(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        friendUsers.findByCode(rawFriendCode)
                .ifPresent(target -> blockRepository.deleteByPair(me.getId(), target.getId()));
    }
}
